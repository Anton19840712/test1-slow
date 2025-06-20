using lazy_light_requests_gate.repositories;
using Npgsql;
using System.Data;
using Serilog;
using Dapper;

namespace lazy_light_requests_gate.middleware;

/// <summary>
/// Единая конфигурация PostgreSQL - объединяет все функции в одном классе
/// </summary>
public static class PostgresDbConfiguration
{
	/// <summary>
	/// Регистрирует все сервисы PostgreSQL в DI контейнере
	/// </summary>
	public static IServiceCollection AddPostgresDbServices(this IServiceCollection services, IConfiguration configuration)
	{
		var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");

		// Получаем секцию настроек PostgreSQL
		var postgresSection = configuration.GetSection("PostgresDbSettings");

		// Проверяем, что секция существует
		if (!postgresSection.Exists())
		{
			Console.WriteLine($"[{timestamp}] [WARNING] PostgresDbSettings не найден в конфигурации");
			return services;
		}

		// Регистрируем настройки
		services.Configure<PostgresDbSettings>(postgresSection);

		// Получаем значения для отладки
		var host = postgresSection.GetValue<string>("Host");
		var port = postgresSection.GetValue<string>("Port");
		var username = postgresSection.GetValue<string>("Username");
		var password = postgresSection.GetValue<string>("Password");
		var database = postgresSection.GetValue<string>("Database");

		// Отладочная информация
		Console.WriteLine($"[{timestamp}] [DEBUG] PostgreSQL конфигурация:");
		Console.WriteLine($"[{timestamp}] [DEBUG] - Host: '{host ?? "NULL"}'");
		Console.WriteLine($"[{timestamp}] [DEBUG] - Port: '{port ?? "NULL"}'");
		Console.WriteLine($"[{timestamp}] [DEBUG] - Username: '{username ?? "NULL"}'");
		Console.WriteLine($"[{timestamp}] [DEBUG] - Password: '{(string.IsNullOrEmpty(password) ? "NULL/EMPTY" : "SET")}'");
		Console.WriteLine($"[{timestamp}] [DEBUG] - Database: '{database ?? "NULL"}'");

		// Проверяем обязательные поля
		if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(username) ||
			string.IsNullOrEmpty(password) || string.IsNullOrEmpty(database))
		{
			throw new InvalidOperationException("Все поля PostgreSQL конфигурации обязательны для заполнения");
		}

		// Формируем строку подключения
		var connectionString = $"Host={host};Port={port ?? "5432"};Username={username};Password={password};Database={database}";
		Console.WriteLine($"[{timestamp}] [DEBUG] - ConnectionString сформирован (длина: {connectionString.Length})");

		// Регистрируем фабрику подключений
		services.AddSingleton<IPostgresConnectionFactory>(provider =>
			new PostgresConnectionFactory(connectionString));

		// Регистрируем IDbConnection для Dapper (Scoped - новое подключение для каждого запроса)
		services.AddScoped<IDbConnection>(sp =>
		{
			var factory = sp.GetRequiredService<IPostgresConnectionFactory>();
			var connection = factory.CreateConnection();
			connection.Open();
			return connection;
		});

		// Регистрируем репозитории PostgreSQL
		services.AddScoped(typeof(IPostgresRepository<>), typeof(PostgresRepository<>));

		Console.WriteLine($"[{timestamp}] [SUCCESS] PostgreSQL сервисы успешно зарегистрированы");
		return services;
	}

	/// <summary>
	/// Инициализирует базу данных PostgreSQL (создает таблицы, заполняет начальные данные)
	/// </summary>
	public static async Task EnsureDatabaseInitializedAsync(IConfiguration configuration)
	{
		var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
		Console.WriteLine($"[{timestamp}] [INFO] Начинается инициализация PostgreSQL базы данных...");

		try
		{
			// Создаем временный сервис провайдер для получения настроек
			var services = new ServiceCollection();
			services.Configure<PostgresDbSettings>(configuration.GetSection("PostgresDbSettings"));
			var serviceProvider = services.BuildServiceProvider();

			var settings = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<PostgresDbSettings>>().Value;
			var connectionString = settings.GetConnectionString();

			// Отладочная информация
			Console.WriteLine($"[{timestamp}] [DEBUG] Подключение к PostgreSQL:");
			Console.WriteLine($"[{timestamp}] [DEBUG] - Host: {settings.Host}");
			Console.WriteLine($"[{timestamp}] [DEBUG] - Database: {settings.Database}");
			Console.WriteLine($"[{timestamp}] [DEBUG] - Username: {settings.Username}");
			Console.WriteLine($"[{timestamp}] [DEBUG] - Password: {(string.IsNullOrEmpty(settings.Password) ? "НЕ УСТАНОВЛЕН" : "УСТАНОВЛЕН")}");

			await using var connection = new NpgsqlConnection(connectionString);
			await connection.OpenAsync();

			Console.WriteLine($"[{timestamp}] [SUCCESS] Подключение к PostgreSQL установлено");

			// Создаем таблицы
			await CreateTablesAsync(connection);

			// Создаем начальные данные (очереди)
			await CreateInitialQueuesAsync(connection, configuration);

			Console.WriteLine($"[{timestamp}] [SUCCESS] Инициализация PostgreSQL завершена успешно");
		}
		catch (Exception ex)
		{
			Console.ForegroundColor = ConsoleColor.Red;
			Console.WriteLine($"[{timestamp}] [ERROR] Ошибка инициализации PostgreSQL: {ex.Message}");
			Console.ResetColor();
			throw;
		}
	}

	/// <summary>
	/// Создает необходимые таблицы в базе данных
	/// </summary>
	private static async Task CreateTablesAsync(NpgsqlConnection connection)
	{
		var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
		Console.WriteLine($"[{timestamp}] [INFO] Начинается создание таблиц PostgreSQL...");

		// 1. Создание базовых таблиц
		var tableSqlDefinitions = new Dictionary<string, string>
		{
			["outbox_messages"] = @"
                CREATE TABLE IF NOT EXISTS outbox_messages (
                    id UUID PRIMARY KEY,
                    model_type INT,
                    event_type INT,
                    is_processed BOOLEAN,
                    processed_at TIMESTAMPTZ,
                    out_queue TEXT,
                    in_queue TEXT,
                    payload TEXT,
                    routing_key TEXT,
                    created_at TIMESTAMPTZ,
                    created_at_formatted TEXT,
                    source TEXT
                );",
			["queues"] = @"
                CREATE TABLE IF NOT EXISTS queues (
                    id UUID PRIMARY KEY,
                    created_at_utc TIMESTAMPTZ,
                    updated_at_utc TIMESTAMPTZ,
                    deleted_at_utc TIMESTAMPTZ,
                    created_by TEXT,
                    updated_by TEXT,
                    deleted_by TEXT,
                    is_deleted BOOLEAN,
                    version INT,
                    ip_address TEXT,
                    user_agent TEXT,
                    correlation_id TEXT,
                    model_type TEXT,
                    is_processed BOOLEAN,
                    created_at_formatted TEXT,
                    in_queue_name TEXT,
                    out_queue_name TEXT
                );",
			["incidents"] = @"
                CREATE TABLE IF NOT EXISTS incidents (
                    id UUID PRIMARY KEY,
                    created_at_utc TIMESTAMPTZ,
                    updated_at_utc TIMESTAMPTZ,
                    deleted_at_utc TIMESTAMPTZ,
                    created_by TEXT,
                    updated_by TEXT,
                    deleted_by TEXT,
                    is_deleted BOOLEAN,
                    version INT,
                    ip_address TEXT,
                    user_agent TEXT,
                    correlation_id TEXT,
                    model_type TEXT,
                    is_processed BOOLEAN,
                    created_at_formatted TEXT,
                    payload TEXT
                );"
		};

		foreach (var (tableName, createSql) in tableSqlDefinitions)
		{
			try
			{
				await connection.ExecuteAsync(createSql);
				Log.Information("Проверена/создана таблица: {Table}", tableName);
			}
			catch (Exception ex)
			{
				Log.Error(ex, "Ошибка при создании таблицы {Table}", tableName);
				throw;
			}
		}

		// 2. Обновление схемы incidents (добавление created_at если нужно)
		await ExecuteSqlSafely(connection, "incidents_created_at_column", @"
            ALTER TABLE incidents 
            ADD COLUMN IF NOT EXISTS created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP;
        ");

		await ExecuteSqlSafely(connection, "incidents_created_at_update", @"
            UPDATE incidents 
            SET created_at = CURRENT_TIMESTAMP 
            WHERE created_at IS NULL;
        ");

		// 3. Обновление схемы outbox_messages (добавление retry полей)
		await ExecuteSqlSafely(connection, "outbox_retry_columns", @"
            ALTER TABLE outbox_messages 
            ADD COLUMN IF NOT EXISTS retry_count INTEGER DEFAULT NULL,
            ADD COLUMN IF NOT EXISTS last_error TEXT DEFAULT NULL,
            ADD COLUMN IF NOT EXISTS last_retry_at TIMESTAMP DEFAULT NULL,
            ADD COLUMN IF NOT EXISTS max_retries INTEGER DEFAULT 3,
            ADD COLUMN IF NOT EXISTS is_dead_letter BOOLEAN DEFAULT FALSE;
        ");

		// 4. Создание индексов для оптимизации
		await ExecuteSqlSafely(connection, "outbox_retry_index", @"
            CREATE INDEX IF NOT EXISTS idx_outbox_retry_lookup 
            ON outbox_messages(is_processed, is_dead_letter, retry_count);
        ");

		await ExecuteSqlSafely(connection, "outbox_cleanup_index", @"
            CREATE INDEX IF NOT EXISTS idx_outbox_cleanup 
            ON outbox_messages(is_processed, processed_at);
        ");

		// 5. Обновление существующих записей в outbox_messages
		await ExecuteSqlSafely(connection, "outbox_update_existing", @"
            UPDATE outbox_messages 
            SET max_retries = 3, is_dead_letter = FALSE 
            WHERE max_retries IS NULL OR is_dead_letter IS NULL;
        ");

		// 6. Создание функции для LISTEN/NOTIFY
		await ExecuteSqlSafely(connection, "notify_function", @"
            CREATE OR REPLACE FUNCTION notify_outbox_message()
            RETURNS TRIGGER AS $
            BEGIN
                PERFORM pg_notify('outbox_new_message', NEW.id::text);
                RETURN NEW;
            END;
            $ LANGUAGE plpgsql;
        ");

		// 7. Создание триггера
		await ExecuteSqlSafely(connection, "drop_trigger", @"
            DROP TRIGGER IF EXISTS outbox_message_notify_trigger ON outbox_messages;
        ");

		await ExecuteSqlSafely(connection, "create_trigger", @"
            CREATE TRIGGER outbox_message_notify_trigger
                AFTER INSERT ON outbox_messages
                FOR EACH ROW
                EXECUTE FUNCTION notify_outbox_message();
        ");

		// 8. Проверка созданных объектов
		await VerifyTriggersAsync(connection);

		Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [SUCCESS] Все таблицы и объекты БД созданы/обновлены");
	}

	/// <summary>
	/// Безопасное выполнение SQL с обработкой ошибок
	/// </summary>
	private static async Task ExecuteSqlSafely(NpgsqlConnection connection, string operationName, string sql)
	{
		try
		{
			await connection.ExecuteAsync(sql);
			Log.Information("Выполнена операция: {Operation}", operationName);
		}
		catch (Exception ex)
		{
			Log.Warning(ex, "Предупреждение при выполнении операции {Operation}: {Message}", operationName, ex.Message);
			// Не бросаем исключение для не критичных операций
		}
	}

	/// <summary>
	/// Проверка созданных триггеров
	/// </summary>
	private static async Task VerifyTriggersAsync(NpgsqlConnection connection)
	{
		try
		{
			const string verificationSql = @"
                SELECT 
                    trigger_name, 
                    event_manipulation, 
                    event_object_table,
                    action_statement
                FROM information_schema.triggers 
                WHERE event_object_table = 'outbox_messages';";

			var triggers = await connection.QueryAsync(verificationSql);

			if (triggers.Any())
			{
				Log.Information("Найдены триггеры для outbox_messages: {Count}", triggers.Count());
				foreach (var trigger in triggers)
				{
					Log.Information("Триггер: {Name} на {Event}", trigger.trigger_name, trigger.event_manipulation);
				}
			}
			else
			{
				Log.Warning("Триггеры для outbox_messages не найдены");
			}
		}
		catch (Exception ex)
		{
			Log.Warning(ex, "Ошибка при проверке триггеров");
		}
	}

	/// <summary>
	/// Создает начальные записи в таблице queues
	/// </summary>
	private static async Task CreateInitialQueuesAsync(NpgsqlConnection connection, IConfiguration configuration)
	{
		var companyName = configuration["CompanyName"] ?? "default-company";
		var queueIn = $"{companyName.Trim().ToLower()}_in";
		var queueOut = $"{companyName.Trim().ToLower()}_out";

		const string insertQueueSql = @"
            INSERT INTO queues (
                id, created_at_utc, is_deleted, version,
                in_queue_name, out_queue_name
            )
            SELECT @Id, @Now, false, 1, @QueueIn, @QueueOut
            WHERE NOT EXISTS (
                SELECT 1 FROM queues WHERE in_queue_name = @QueueIn AND out_queue_name = @QueueOut
            );";

		try
		{
			await connection.ExecuteAsync(insertQueueSql, new
			{
				Id = Guid.NewGuid(),
				Now = DateTime.UtcNow,
				QueueIn = queueIn,
				QueueOut = queueOut
			});

			Log.Information("Очереди {QueueIn} и {QueueOut} проверены/созданы в таблице queues", queueIn, queueOut);
		}
		catch (Exception ex)
		{
			Log.Warning(ex, "Не удалось вставить название очередей {QueueIn}/{QueueOut}", queueIn, queueOut);
			throw;
		}
	}
}

/// <summary>
/// Фабрика для создания подключений к PostgreSQL
/// </summary>
public interface IPostgresConnectionFactory
{
	string GetConnectionString();
	IDbConnection CreateConnection();
}

/// <summary>
/// Реализация фабрики подключений к PostgreSQL
/// </summary>
public class PostgresConnectionFactory : IPostgresConnectionFactory
{
	private readonly string _connectionString;

	public PostgresConnectionFactory(string connectionString)
	{
		_connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
	}

	public string GetConnectionString() => _connectionString;

	public IDbConnection CreateConnection() => new NpgsqlConnection(_connectionString);
}