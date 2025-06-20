namespace lazy_light_requests_gate.repositories
{
	public interface IPostgresRepository<T> : IBaseRepository<T> where T : class
	{
		Task<T> GetByIdAsync(Guid id);
		Task<IEnumerable<T>> FindAsync(string whereClause, object parameters = null);
		Task UpdateAsync(Guid id, T entity);
		Task DeleteByIdAsync(Guid id);
		Task<int> DeleteByTtlAsync(TimeSpan olderThan);
	}
}
