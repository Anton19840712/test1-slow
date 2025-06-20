using lazy_light_requests_gate.entities;
using lazy_light_requests_gate.middleware;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Serilog;
using System.Linq.Expressions;

namespace lazy_light_requests_gate.repositories
{
	public class MongoRepository<T> : IMongoRepository<T> where T : class
	{
		private readonly IMongoCollection<T> _collection;

		public MongoRepository(
			IMongoClient mongoClient,
			IOptions<MongoDbSettings> settings)
		{
			var database = mongoClient.GetDatabase(settings.Value.DatabaseName);

			string collectionName = GetCollectionName(typeof(T), settings.Value);
			_collection = database.GetCollection<T>(collectionName);

			try
			{
				database.ListCollectionNames().ToList(); // Простой запрос для проверки подключения
				Log.Information($"Успешное подключение к MongoDB в базе: {settings.Value.DatabaseName}, коллекция: {collectionName}");
			}
			catch (Exception ex)
			{
				// Логируем ошибку, если подключение не удалось
				Log.Error($"Ошибка при подключении к MongoDB в базе: {settings.Value.DatabaseName}, коллекция: {collectionName}. Ошибка: {ex.Message}");
				throw new InvalidOperationException("Не удалось подключиться к MongoDB", ex);
			}
		}

		private string GetCollectionName(Type entityType, MongoDbSettings settings)
		{
			return entityType.Name switch
			{
				nameof(OutboxMessage) => settings.Collections.OutboxCollection,
				nameof(QueuesEntity) => settings.Collections.QueueCollection,
				nameof(IncidentEntity) => settings.Collections.IncidentCollection,
				_ => throw new ArgumentException($"Неизвестный тип {entityType.Name}")
			};
		}

		public async Task<T> GetByIdAsync(string id) =>
			await _collection.Find(Builders<T>.Filter.Eq("_id", id)).FirstOrDefaultAsync();

		public async Task<IEnumerable<T>> GetAllAsync() =>
			await _collection.Find(_ => true).ToListAsync();

		public async Task<IEnumerable<T>> FindAsync(Expression<Func<T, bool>> filter) =>
			await _collection.Find(filter).ToListAsync();

		public async Task InsertAsync(T entity) =>
			await _collection.InsertOneAsync(entity);

		public async Task UpdateAsync(string id, T updatedEntity)
		{
			var filter = Builders<T>.Filter.Eq("_id", ObjectId.Parse(id));
			var existingEntity = await _collection.Find(filter).FirstOrDefaultAsync();

			if (existingEntity == null)
			{
				throw new InvalidOperationException($"Документ с ID {id} не найден");
			}

			var updateDefinitionBuilder = Builders<T>.Update;
			var updates = new List<UpdateDefinition<T>>();

			// Перебираем все свойства модели
			foreach (var property in typeof(T).GetProperties())
			{
				if (property.Name == "Version") continue; // Пропускаем Version, т.к. он изменяется отдельно

				var oldValue = property.GetValue(existingEntity);
				var newValue = property.GetValue(updatedEntity);

				if (newValue != null && !newValue.Equals(oldValue))
				{
					updates.Add(updateDefinitionBuilder.Set(property.Name, newValue));
				}
			}

			// Добавляем обновление времени
			updates.Add(updateDefinitionBuilder.Set("UpdatedAtUtc", DateTime.UtcNow));
			updates.Add(updateDefinitionBuilder.Inc("Version", 1));

			if (updates.Count > 0)
			{
				var updateDefinition = updateDefinitionBuilder.Combine(updates);
				await _collection.UpdateOneAsync(filter, updateDefinition);
			}
		}

		public async Task DeleteByIdAsync(string id) =>
			await _collection.DeleteOneAsync(Builders<T>.Filter.Eq("_id", id));

		public async Task<int> DeleteByTtlAsync(TimeSpan olderThan)
		{
			if (typeof(T) != typeof(OutboxMessage))
			{
				throw new InvalidOperationException("Метод поддерживает только OutboxMessage.");
			}

			var filter = Builders<OutboxMessage>.Filter.And(
				Builders<OutboxMessage>.Filter.Lt(m => m.CreatedAt, DateTime.UtcNow - olderThan),
				Builders<OutboxMessage>.Filter.Eq(m => m.IsProcessed, true)
			);

			var collection = _collection as IMongoCollection<OutboxMessage>;
			var result = await collection.DeleteManyAsync(filter);

			return (int)result.DeletedCount;
		}
		public async Task<List<T>> GetUnprocessedMessagesAsync()
		{
			var filter = Builders<T>.Filter.Eq("IsProcessed", false);
			return await _collection.Find(filter).ToListAsync();
		}

		public async Task MarkMessageAsProcessedAsync(Guid messageId)
		{
			var update = Builders<T>.Update
				.Set("IsProcessed", true)
				.Set("ProcessedAt", DateTime.UtcNow);

			await _collection.UpdateOneAsync(Builders<T>.Filter.Eq("Id", messageId), update);
		}

		public async Task<int> DeleteOldMessagesAsync(TimeSpan olderThan)
		{
			var filter = Builders<T>.Filter.And(
				Builders<T>.Filter.Lt("CreatedAt", DateTime.UtcNow - olderThan),
				Builders<T>.Filter.Eq("IsProcessed", true)
			);

			var result = await _collection.DeleteManyAsync(filter);
			return (int)result.DeletedCount;
		}

		public async Task SaveMessageAsync(T message)
		{
			await _collection.InsertOneAsync(message);
		}

		public async Task UpdateMessageAsync(T message)
		{
			if (message is OutboxMessage outboxMessage)
			{
				var filter = Builders<T>.Filter.Eq("Id", outboxMessage.Id);
				var update = Builders<T>.Update
					.Set("IsProcessed", outboxMessage.IsProcessed)
					.Set("ProcessedAt", outboxMessage.ProcessedAt);

				await _collection.UpdateOneAsync(filter, update);
			}
			else
			{
				throw new InvalidOperationException("UpdateMessageAsync поддерживает только OutboxMessage");
			}
		}

		public IMongoCollection<T> GetCollection()
		{
			return _collection;
		}

		public async Task UpdateMessagesAsync(IEnumerable<T> messages)
		{
			if (!messages?.Any() ?? true) return;

			var bulkOps = new List<WriteModel<T>>();

			foreach (var message in messages)
			{
				if (message is OutboxMessage outboxMessage)
				{
					var filter = Builders<T>.Filter.Eq("Id", outboxMessage.Id);
					var update = Builders<T>.Update
						.Set("IsProcessed", outboxMessage.IsProcessed)
						.Set("ProcessedAt", outboxMessage.ProcessedAt)
						.Set("UpdatedAtUtc", DateTime.UtcNow);

					bulkOps.Add(new UpdateOneModel<T>(filter, update));
				}
				else
				{
					// Для других типов - более общий подход
					var messageId = GetMessageId(message);
					var filter = Builders<T>.Filter.Eq("Id", messageId);

					var updateBuilder = Builders<T>.Update;
					var updates = new List<UpdateDefinition<T>>();

					// Обновляем все свойства кроме Id и CreatedAt
					foreach (var property in typeof(T).GetProperties())
					{
						if (property.Name == "Id" || property.Name == "CreatedAt")
							continue;

						var value = property.GetValue(message);
						if (value != null)
						{
							updates.Add(updateBuilder.Set(property.Name, value));
						}
					}

					updates.Add(updateBuilder.Set("UpdatedAtUtc", DateTime.UtcNow));

					if (updates.Any())
					{
						var combinedUpdate = updateBuilder.Combine(updates);
						bulkOps.Add(new UpdateOneModel<T>(filter, combinedUpdate));
					}
				}
			}

			if (bulkOps.Any())
			{
				var options = new BulkWriteOptions { IsOrdered = false };
				var result = await _collection.BulkWriteAsync(bulkOps, options);

				Log.Information("MongoDB batch update completed: {ModifiedCount} updated, {UpsertedCount} upserted",
					result.ModifiedCount, result.Upserts.Count);
			}
		}

		// Вспомогательный метод для MongoDB
		private Guid GetMessageId(T message)
		{
			var idProperty = typeof(T).GetProperty("Id");
			if (idProperty != null && idProperty.PropertyType == typeof(Guid))
			{
				return (Guid)idProperty.GetValue(message);
			}
			throw new InvalidOperationException($"Не найдено свойство Id типа Guid в {typeof(T).Name}");
		}

		public async Task InsertMessagesAsync(IEnumerable<T> messages)
		{
			if (messages == null || !messages.Any())
				return;

			await _collection.InsertManyAsync(messages);
		}

		public async Task DeleteMessagesAsync(IEnumerable<Guid> ids)
		{
			if (ids == null || !ids.Any())
				return;

			var filter = Builders<T>.Filter.In("Id", ids);
			await _collection.DeleteManyAsync(filter);
		}

	}
}