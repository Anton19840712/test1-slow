using System.Data;
using System.Reflection;
using Dapper;
using lazy_light_requests_gate.entities;
using lazy_light_requests_gate.middleware;
using Microsoft.Extensions.Options;
using Npgsql;
using Polly;
using Polly.Retry;
using Serilog;

namespace lazy_light_requests_gate.repositories;

public class PostgresRepository<T> : IPostgresRepository<T> where T : class
{
	private readonly string _connectionString;
	private readonly string _tableName;
	private readonly AsyncRetryPolicy _retryPolicy;

	public PostgresRepository(IOptions<PostgresDbSettings> settings)
	{
		_connectionString = settings.Value.GetConnectionString();
		_tableName = GetTableName(typeof(T));
		_retryPolicy = Policy
			.Handle<NpgsqlException>()
			.WaitAndRetryAsync(3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
				onRetry: (exception, timeSpan, retryCount, _) =>
				{
					Log.Warning($"Ошибка PostgreSQL, попытка {retryCount}, повтор через {timeSpan.TotalSeconds} сек. Причина: {exception.Message}");
				});

		try
		{
			using var connection = new NpgsqlConnection(_connectionString);
			connection.Open();
			Log.Information($"Успешное подключение к PostgreSQL, таблица: {_tableName}");
		}
		catch (Exception ex)
		{
			Log.Error($"Ошибка подключения к PostgreSQL: {ex.Message}");
			throw new InvalidOperationException("Не удалось подключиться к PostgreSQL", ex);
		}
	}

	private string GetTableName(Type type) => type.Name switch
	{
		nameof(OutboxMessage) => "outbox_messages",
		nameof(QueuesEntity) => "queues",
		nameof(IncidentEntity) => "incidents",
		_ => throw new ArgumentException($"Неизвестный тип: {type.Name}")
	};

	private IDbConnection CreateConnection() => new NpgsqlConnection(_connectionString);

	public async Task<T> GetByIdAsync(Guid id)
	{
		using var connection = CreateConnection();
		var sql = $"SELECT * FROM {_tableName} WHERE id = @id";
		return await _retryPolicy.ExecuteAsync(() => connection.QueryFirstOrDefaultAsync<T>(sql, new { id }));
	}

	public async Task<IEnumerable<T>> GetAllAsync()
	{
		using var connection = CreateConnection();
		var sql = $"SELECT * FROM {_tableName}";
		return await _retryPolicy.ExecuteAsync(() => connection.QueryAsync<T>(sql));
	}

	public async Task<IEnumerable<T>> FindAsync(string whereClause, object parameters = null)
	{
		using var connection = CreateConnection();
		var sql = $"SELECT * FROM {_tableName} WHERE {whereClause}";
		return await _retryPolicy.ExecuteAsync(() => connection.QueryAsync<T>(sql, parameters));
	}

	public async Task InsertAsync(T entity)
	{
		using var connection = CreateConnection();
		var sql = GenerateInsertSql(entity);
		await _retryPolicy.ExecuteAsync(() => connection.ExecuteAsync(sql, entity));
	}

	public async Task UpdateAsync(Guid id, T updatedEntity)
	{
		using var connection = CreateConnection();
		var sql = GenerateUpdateSql(updatedEntity, id);
		await _retryPolicy.ExecuteAsync(() => connection.ExecuteAsync(sql, updatedEntity));
	}

	public async Task DeleteByIdAsync(Guid id)
	{
		using var connection = CreateConnection();
		var sql = $"DELETE FROM {_tableName} WHERE id = @id";
		await _retryPolicy.ExecuteAsync(() => connection.ExecuteAsync(sql, new { id }));
	}

	public async Task<int> DeleteByTtlAsync(TimeSpan olderThan)
	{
		if (typeof(T) != typeof(OutboxMessage))
			throw new InvalidOperationException("Метод поддерживает только OutboxMessage");

		using var connection = CreateConnection();
		var sql = $"DELETE FROM {_tableName} WHERE created_at < @cutoff AND is_processed = true";
		return await _retryPolicy.ExecuteAsync(() => connection.ExecuteAsync(sql, new { cutoff = DateTime.UtcNow - olderThan }));
	}

	public async Task<List<T>> GetUnprocessedMessagesAsync()
	{
		using var connection = CreateConnection();
		var sql = $"SELECT * FROM {_tableName} WHERE is_processed = false";
		var result = await _retryPolicy.ExecuteAsync(() => connection.QueryAsync<T>(sql));
		return result.ToList();
	}

	public async Task MarkMessageAsProcessedAsync(Guid messageId)
	{
		using var connection = CreateConnection();
		var sql = $@"
		UPDATE {_tableName}
		SET is_processed = true,
			processed_at = @now
		WHERE id = @messageId";

		await _retryPolicy.ExecuteAsync(() =>
			connection.ExecuteAsync(sql, new { messageId, now = DateTime.UtcNow }));
	}



	public async Task<int> DeleteOldMessagesAsync(TimeSpan olderThan)
	{
		using var connection = CreateConnection();
		var sql = $"DELETE FROM {_tableName} WHERE created_at < @cutoff AND is_processed = true";
		return await _retryPolicy.ExecuteAsync(() => connection.ExecuteAsync(sql, new { cutoff = DateTime.UtcNow - olderThan }));
	}

	public async Task SaveMessageAsync(T message) => await InsertAsync(message);

	public async Task UpdateMessageAsync(T message)
	{
		if (message is OutboxMessage outboxMessage)
		{
			const string sql = @"
				UPDATE outbox_messages 
				SET is_processed = @IsProcessed, processed_at = @ProcessedAt 
				WHERE id = @Id";

			using var connection = CreateConnection();
			await _retryPolicy.ExecuteAsync(() => connection.ExecuteAsync(sql, outboxMessage));
		}
		else
		{
			throw new InvalidOperationException("UpdateMessageAsync поддерживает только OutboxMessage");
		}
	}

	private string GenerateInsertSql(T entity)
	{
		var props = typeof(T).GetProperties()
			.Where(p => !IsNotMapped(p))
			.ToList();

		var columns = string.Join(", ", props.Select(p => ToSnakeCase(p.Name)));
		var values = string.Join(", ", props.Select(p => "@" + p.Name));

		return $"INSERT INTO {_tableName} ({columns}) VALUES ({values})";
	}

	private bool IsNotMapped(PropertyInfo prop) =>
		Attribute.IsDefined(prop, typeof(System.ComponentModel.DataAnnotations.Schema.NotMappedAttribute));

	private string GenerateUpdateSql(T entity, Guid id)
	{
		var props = typeof(T).GetProperties()
			.Where(p => !IsNotMapped(p))
			.ToList();

		// Исключаем updated_at, так как его нет в таблице
		props = props.Where(p => !string.Equals(p.Name, "UpdatedAt", StringComparison.OrdinalIgnoreCase)).ToList();

		var sets = string.Join(", ", props.Select(p => $"{ToSnakeCase(p.Name)} = @{p.Name}"));

		return $"UPDATE {_tableName} SET {sets} WHERE id = @Id";
	}


	private string ToSnakeCase(string input)
	{
		return string.Concat(input.Select((c, i) =>
			i > 0 && char.IsUpper(c) ? "_" + char.ToLower(c) : char.ToLower(c).ToString()));
	}
	public async Task UpdateMessagesAsync(IEnumerable<T> messages)
	{
		if (!messages?.Any() ?? true) return;

		var messagesList = messages.ToList();

		if (typeof(T) == typeof(OutboxMessage))
		{
			await UpdateOutboxMessagesBatch(messagesList.Cast<OutboxMessage>());
		}
		else
		{
			await UpdateMessagesGeneric(messagesList);
		}
	}

	private async Task UpdateOutboxMessagesBatch(IEnumerable<OutboxMessage> messages)
	{
		const string sql = @"
        UPDATE outbox_messages 
        SET 
            is_processed = data.is_processed,
            processed_at = data.processed_at
        FROM (
            SELECT 
                unnest(@Ids) AS id,
                unnest(@IsProcessed) AS is_processed,
                unnest(@ProcessedAt) AS processed_at
        ) AS data
        WHERE outbox_messages.id = data.id";

		var messagesList = messages.ToList();

		var parameters = new
		{
			Ids = messagesList.Select(m => m.Id).ToArray(),
			IsProcessed = messagesList.Select(m => m.IsProcessed).ToArray(),
			ProcessedAt = messagesList.Select(m => m.ProcessedAt).ToArray()
		};

		using var connection = CreateConnection();
		var affectedRows = await _retryPolicy.ExecuteAsync(() =>
			connection.ExecuteAsync(sql, parameters));

		Log.Information("PostgreSQL batch update completed: {AffectedRows} rows updated", affectedRows);
	}



	private async Task UpdateMessagesGeneric(IEnumerable<T> messages)
	{
		using var connection = CreateConnection();
		connection.Open();

		using var transaction = (NpgsqlTransaction)connection.BeginTransaction();

		try
		{
			var updateCount = 0;

			foreach (var message in messages)
			{
				var messageId = GetMessageIdGeneric(message);
				var sql = GenerateUpdateSql(message, messageId);

				var rowsAffected = await connection.ExecuteAsync(sql, message, transaction);
				updateCount += rowsAffected;
			}

			await transaction.CommitAsync();

			Log.Information("PostgreSQL generic batch update completed: {UpdateCount} messages updated", updateCount);
		}
		catch (Exception ex)
		{
			await transaction.RollbackAsync();
			Log.Error(ex, "Error during batch update, transaction rolled back");
			throw;
		}
	}

	// Вспомогательный метод для PostgreSQL
	private Guid GetMessageIdGeneric(T message)
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

		using var connection = CreateConnection();
		connection.Open();

		using var transaction = connection.BeginTransaction();

		try
		{
			foreach (var message in messages)
			{
				var sql = GenerateInsertSql(message);
				await connection.ExecuteAsync(sql, message, transaction);
			}

			transaction.Commit();
		}
		catch
		{
			transaction.Rollback();
			throw;
		}
	}

	public async Task DeleteMessagesAsync(IEnumerable<Guid> ids)
	{
		if (ids == null || !ids.Any())
			return;

		using var connection = CreateConnection();
		var sql = $"DELETE FROM {_tableName} WHERE id = ANY(@Ids)";
		await connection.ExecuteAsync(sql, new { Ids = ids.ToArray() });
	}
}
