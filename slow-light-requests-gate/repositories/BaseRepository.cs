namespace lazy_light_requests_gate.repositories
{
	public abstract class BaseRepository<T> : IBaseRepository<T> where T : class
	{
		public abstract Task<IEnumerable<T>> GetAllAsync();
		public abstract Task InsertAsync(T entity);

		public virtual async Task<List<T>> GetUnprocessedMessagesAsync()
		{
			var messages = await GetUnprocessedMessagesInternalAsync();
			return messages.ToList();
		}

		public virtual async Task MarkMessageAsProcessedAsync(Guid messageId)
		{
			await MarkMessageAsProcessedInternalAsync(messageId);
		}

		public virtual async Task<int> DeleteOldMessagesAsync(TimeSpan olderThan)
		{
			return await DeleteOldMessagesInternalAsync(olderThan);
		}

		public virtual async Task SaveMessageAsync(T message)
		{
			await InsertAsync(message);
		}

		public abstract Task UpdateMessageAsync(T message);

		public abstract Task UpdateMessagesAsync(IEnumerable<T> messages);
		public abstract Task InsertMessagesAsync(IEnumerable<T> messages);
		public abstract Task DeleteMessagesAsync(IEnumerable<Guid> messageIds);

		// Абстрактные методы для специфичной реализации
		protected abstract Task<IEnumerable<T>> GetUnprocessedMessagesInternalAsync();
		protected abstract Task MarkMessageAsProcessedInternalAsync(Guid messageId);
		protected abstract Task<int> DeleteOldMessagesInternalAsync(TimeSpan olderThan);
	}
}
