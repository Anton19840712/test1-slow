using System.Linq.Expressions;
using MongoDB.Driver;

namespace lazy_light_requests_gate.repositories
{
	public interface IMongoRepository<T> : IBaseRepository<T> where T : class
	{
		Task<T> GetByIdAsync(string id);
		Task<IEnumerable<T>> FindAsync(Expression<Func<T, bool>> filter);
		Task UpdateAsync(string id, T entity);
		Task DeleteByIdAsync(string id);
		Task<int> DeleteByTtlAsync(TimeSpan olderThan);
		IMongoCollection<T> GetCollection();
	}
}
