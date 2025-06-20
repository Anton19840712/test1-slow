using Newtonsoft.Json.Linq;
using System.ComponentModel.DataAnnotations;

namespace lazy_light_requests_gate.middleware;

/// <summary>
/// Класс используется для предоставления возможности настройщику системы
/// динамически задавать хост и порт самого динамического шлюза.
/// </summary>
public static class GateConfiguration
{
	/// <summary>
	/// Настройка динамических параметров шлюза и возврат HTTP/HTTPS адресов
	/// </summary>
	public static async Task<(string HttpUrl, string HttpsUrl)> ConfigureDynamicGateAsync(string[] args, WebApplicationBuilder builder)
	{
		var configFilePath = args.FirstOrDefault(a => a.StartsWith("--config="))?.Substring(9) ?? "gate-config.json";
		var config = LoadConfiguration(configFilePath);

		// Валидируем базовую структуру конфигурации
		ValidateBasicConfiguration(config);

		var configType = config["type"]?.ToString() ?? config["Type"]?.ToString();
		return await ConfigureRestGate(config, builder);
	}

	private static async Task<(string HttpUrl, string HttpsUrl)> ConfigureRestGate(JObject config, WebApplicationBuilder builder)
	{
		var companyName = config["CompanyName"]?.ToString() ?? "default-company";
		var host = config["Host"]?.ToString() ?? "localhost";
		var port = int.TryParse(config["Port"]?.ToString(), out var p) ? p : 5000;
		var enableValidation = bool.TryParse(config["Validate"]?.ToString(), out var v) && v;
		var database = config["Database"]?.ToString() ?? "mongo";
		var bus = config["Bus"]?.ToString() ?? "rabbit";
		var cleanupIntervalSeconds = int.TryParse(config["CleanupIntervalSeconds"]?.ToString(), out var c) ? c : 10;
		var outboxMessageTtlSeconds = int.TryParse(config["OutboxMessageTtlSeconds"]?.ToString(), out var ttl) ? ttl : 10;
		var incidentEntitiesTtlMonths = int.TryParse(config["IncidentEntitiesTtlMonths"]?.ToString(), out var incident) ? incident : 10;

		// Валидируем выбранные параметры
		ValidateConfiguration(database, bus, config);

		// Парсим в strongly typed модель для дополнительной валидации
		var gateConfig = ParseToStronglyTypedModel(config);
		ValidateGateConfigurationModel(gateConfig);

		// Настройка шины сообщений
		var busSettings = bus switch
		{
			"rabbit" => config["RabbitMqSettings"],
			"activemq" => config["ActiveMqSettings"],
			"kafka" => config["KafkaStreamsSettings"],
			_ => throw new InvalidOperationException($"Bus '{bus}' не поддерживается. Поддерживаются: rabbit, activemq, kafka.")
		};

		if (busSettings is JObject settingsObject)
		{
			foreach (var prop in settingsObject.Properties())
			{
				var key = $"BusSettings:{prop.Name}";
				var value = prop.Value?.ToString();
				if (!string.IsNullOrEmpty(value))
					builder.Configuration[key] = value;
			}
		}

		// Основные параметры конфигурации
		builder.Configuration["CompanyName"] = companyName;
		builder.Configuration["Host"] = host;
		builder.Configuration["Port"] = port.ToString();
		builder.Configuration["Validate"] = enableValidation.ToString();
		builder.Configuration["Database"] = database;
		builder.Configuration["Bus"] = bus;
		builder.Configuration["CleanupIntervalSeconds"] = cleanupIntervalSeconds.ToString();
		builder.Configuration["OutboxMessageTtlSeconds"] = outboxMessageTtlSeconds.ToString();
		builder.Configuration["IncidentEntitiesTtlMonths"] = incidentEntitiesTtlMonths.ToString();

		// Настройка базы данных
		await ConfigureDatabase(config, builder, database);

		// Настройка шины сообщений  
		await ConfigureMessageBus(config, builder, bus);

		// Красивое логирование конфигурации
		LogDetailedConfiguration(companyName, host, port, enableValidation, database, bus,
			cleanupIntervalSeconds, outboxMessageTtlSeconds, incidentEntitiesTtlMonths, config);

		// ports here were hardcoded:
		var httpUrl = $"http://{host}:80";
		var httpsUrl = $"https://{host}:443";
		return await Task.FromResult((httpUrl, httpsUrl));
	}

	private static void ValidateBasicConfiguration(JObject config)
	{
		var requiredFields = new[] { "CompanyName", "Host", "Port", "Database", "Bus" };
		var missingFields = requiredFields.Where(field =>
			string.IsNullOrEmpty(config[field]?.ToString())).ToList();

		if (missingFields.Any())
		{
			throw new InvalidOperationException($"Отсутствуют обязательные поля в конфигурации: {string.Join(", ", missingFields)}");
		}
	}

	private static void ValidateConfiguration(string database, string bus, JObject config)
	{
		// Валидация базы данных
		if (!new[] { "postgres", "mongo" }.Contains(database.ToLower()))
		{
			throw new InvalidOperationException($"Неподдерживаемая база данных: {database}. Поддерживаются: postgres, mongo");
		}

		// Валидация шины сообщений
		if (!new[] { "rabbit", "activemq", "kafka" }.Contains(bus.ToLower()))
		{
			throw new InvalidOperationException($"Неподдерживаемая шина сообщений: {bus}. Поддерживаются: rabbit, activemq, kafka");
		}

		// Проверка наличия настроек для выбранной БД (только для активной)
		if (database == "postgres" && config["PostgresDbSettings"] == null)
		{
			throw new InvalidOperationException("PostgresDbSettings обязательны когда Database = 'postgres'");
		}

		if (database == "mongo" && config["MongoDbSettings"] == null)
		{
			throw new InvalidOperationException("MongoDbSettings обязательны когда Database = 'mongo'");
		}

		// Проверка наличия настроек для выбранной шины
		var busSettingsKey = bus switch
		{
			"rabbit" => "RabbitMqSettings",
			"activemq" => "ActiveMqSettings",
			"kafka" => "KafkaStreamsSettings",
			_ => null
		};

		if (busSettingsKey != null && config[busSettingsKey] == null)
		{
			throw new InvalidOperationException($"{busSettingsKey} обязательны когда Bus = '{bus}'");
		}
	}

	private static GateConfigurationModel ParseToStronglyTypedModel(JObject config)
	{
		try
		{
			return config.ToObject<GateConfigurationModel>()
				?? throw new InvalidOperationException("Не удалось распарсить конфигурацию в типизированную модель");
		}
		catch (Exception ex)
		{
			throw new InvalidOperationException($"Ошибка парсинга конфигурации в типизированную модель: {ex.Message}");
		}
	}

	private static void ValidateGateConfigurationModel(GateConfigurationModel config)
	{
		var context = new ValidationContext(config);
		var results = new List<ValidationResult>();

		if (!Validator.TryValidateObject(config, context, results, true))
		{
			var errors = string.Join(", ", results.Select(r => r.ErrorMessage));
			throw new InvalidOperationException($"Ошибки валидации типизированной модели: {errors}");
		}
	}

	private static async Task ConfigureDatabase(JObject config, WebApplicationBuilder builder, string database)
	{
		// Настраиваем PostgreSQL, если настройки есть в конфигурации
		if (config["PostgresDbSettings"] != null)
		{
			ConfigurePostgreSQL(config, builder);
			var timestamp1 = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
			Console.WriteLine($"[{timestamp1}] [CONFIG] ✓ PostgreSQL база данных настроена");
		}

		// Настраиваем MongoDB, если настройки есть в конфигурации
		if (config["MongoDbSettings"] != null)
		{
			ConfigureMongoDB(config, builder);
			var timestamp2 = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
			Console.WriteLine($"[{timestamp2}] [CONFIG] ✓ MongoDB база данных настроена");
		}

		// Логируем, какая база выбрана как основная
		var timestamp3 = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
		Console.ForegroundColor = ConsoleColor.Yellow;
		Console.WriteLine($"[{timestamp3}] [CONFIG] ⚡ Активная база данных: {database.ToUpper()}");
		Console.ResetColor();

		// Проверяем, что основная база настроена
		switch (database.ToLower())
		{
			case "postgres" when config["PostgresDbSettings"] == null:
				throw new InvalidOperationException("PostgresDbSettings обязательны когда Database = 'postgres'");
			case "mongo" when config["MongoDbSettings"] == null:
				throw new InvalidOperationException("MongoDbSettings обязательны когда Database = 'mongo'");
		}
	}

	private static void ConfigurePostgreSQL(JObject config, WebApplicationBuilder builder)
	{
		var postgresSettings = config["PostgresDbSettings"];
		if (postgresSettings != null)
		{
			var host = postgresSettings["Host"]?.ToString() ?? "localhost";
			var port = postgresSettings["Port"]?.ToString() ?? "5432";
			var username = postgresSettings["Username"]?.ToString() ?? "postgres";
			var password = postgresSettings["Password"]?.ToString() ?? "";
			var database = postgresSettings["Database"]?.ToString() ?? "GatewayDB";

			builder.Configuration["PostgresDbSettings:Host"] = host;
			builder.Configuration["PostgresDbSettings:Port"] = port;
			builder.Configuration["PostgresDbSettings:Username"] = username;
			builder.Configuration["PostgresDbSettings:Password"] = password;
			builder.Configuration["PostgresDbSettings:Database"] = database;

			// Отладочное логирование (БЕЗ пароля в логах!)
			var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
			Console.WriteLine($"[{timestamp}] [DEBUG] PostgreSQL настройки:");
			Console.WriteLine($"[{timestamp}] [DEBUG] - Host: {host}");
			Console.WriteLine($"[{timestamp}] [DEBUG] - Port: {port}");
			Console.WriteLine($"[{timestamp}] [DEBUG] - Username: {username}");
			Console.WriteLine($"[{timestamp}] [DEBUG] - Database: {database}");
			Console.WriteLine($"[{timestamp}] [DEBUG] - Password: {(string.IsNullOrEmpty(password) ? "ПУСТОЙ" : "УСТАНОВЛЕН")}");

			// Проверяем итоговую строку подключения
			if (!string.IsNullOrEmpty(password))
			{
				var testConnectionString = $"Host={host};Port={port};Username={username};Password={password};Database={database}";
				Console.WriteLine($"[{timestamp}] [DEBUG] - Connection String сформирован успешно (длина: {testConnectionString.Length})");
			}
			else
			{
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine($"[{timestamp}] [ERROR] - ПАРОЛЬ НЕ НАЙДЕН В КОНФИГУРАЦИИ!");
				Console.ResetColor();
			}
		}
		else
		{
			var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
			Console.ForegroundColor = ConsoleColor.Red;
			Console.WriteLine($"[{timestamp}] [ERROR] - PostgresDbSettings отсутствует в конфигурации!");
			Console.ResetColor();
		}
	}

	private static void ConfigureMongoDB(JObject config, WebApplicationBuilder builder)
	{
		var mongoSettings = config["MongoDbSettings"];
		if (mongoSettings != null)
		{
			var connectionString = mongoSettings["ConnectionString"]?.ToString() ?? "";
			var databaseName = mongoSettings["DatabaseName"]?.ToString() ?? "GatewayDB";

			// Извлекаем User и Password из ConnectionString, если они не указаны отдельно
			var user = mongoSettings["User"]?.ToString() ?? ExtractUserFromConnectionString(connectionString);
			var password = mongoSettings["Password"]?.ToString() ?? ExtractPasswordFromConnectionString(connectionString);

			// Основные настройки
			builder.Configuration["MongoDbSettings:ConnectionString"] = connectionString;
			builder.Configuration["MongoDbSettings:DatabaseName"] = databaseName;
			builder.Configuration["MongoDbSettings:User"] = user;
			builder.Configuration["MongoDbSettings:Password"] = password;

			// Коллекции
			var collections = mongoSettings["Collections"];
			if (collections != null)
			{
				builder.Configuration["MongoDbSettings:Collections:QueueCollection"] = collections["QueueCollection"]?.ToString() ?? "QueueEntities";
				builder.Configuration["MongoDbSettings:Collections:OutboxCollection"] = collections["OutboxCollection"]?.ToString() ?? "OutboxMessages";
				builder.Configuration["MongoDbSettings:Collections:IncidentCollection"] = collections["IncidentCollection"]?.ToString() ?? "IncidentEntities";
			}

			// Отладочная информация (БЕЗ пароля в логах!)
			var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
			Console.WriteLine($"[{timestamp}] [DEBUG] MongoDB настройки:");
			Console.WriteLine($"[{timestamp}] [DEBUG] - ConnectionString: {(string.IsNullOrEmpty(connectionString) ? "ПУСТАЯ" : "УСТАНОВЛЕНА")}");
			Console.WriteLine($"[{timestamp}] [DEBUG] - DatabaseName: {databaseName}");
			Console.WriteLine($"[{timestamp}] [DEBUG] - User: {user ?? "НЕ УСТАНОВЛЕН"}");
			Console.WriteLine($"[{timestamp}] [DEBUG] - Password: {(string.IsNullOrEmpty(password) ? "НЕ УСТАНОВЛЕН" : "УСТАНОВЛЕН")}");
		}
		else
		{
			var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
			Console.ForegroundColor = ConsoleColor.Red;
			Console.WriteLine($"[{timestamp}] [ERROR] - MongoDbSettings отсутствует в конфигурации!");
			Console.ResetColor();
		}
	}

	// Добавьте эти вспомогательные методы в класс GateConfiguration
	private static string ExtractUserFromConnectionString(string connectionString)
	{
		if (string.IsNullOrEmpty(connectionString))
			return "";

		try
		{
			var uri = new Uri(connectionString);
			return uri.UserInfo?.Split(':')[0] ?? "";
		}
		catch
		{
			return "";
		}
	}

	private static string ExtractPasswordFromConnectionString(string connectionString)
	{
		if (string.IsNullOrEmpty(connectionString))
			return "";

		try
		{
			var uri = new Uri(connectionString);
			var userInfo = uri.UserInfo?.Split(':');
			return userInfo?.Length > 1 ? userInfo[1] : "";
		}
		catch
		{
			return "";
		}
	}
	private static async Task ConfigureMessageBus(JObject config, WebApplicationBuilder builder, string bus)
	{
		switch (bus.ToLower())
		{
			case "rabbit":
				ConfigureRabbitMQ(config, builder);
				var timestamp1 = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
				Console.WriteLine($"[{timestamp1}] [CONFIG] ✓ RabbitMQ шина сообщений настроена");
				break;

			case "activemq":
				ConfigureActiveMQ(config, builder);
				var timestamp2 = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
				Console.WriteLine($"[{timestamp2}] [CONFIG] ✓ ActiveMQ шина сообщений настроена");
				break;

			case "kafka":
				ConfigureKafka(config, builder);
				var timestamp3 = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
				Console.WriteLine($"[{timestamp3}] [CONFIG] ✓ Kafka шина сообщений настроена");
				break;
		}
	}

	private static void ConfigureRabbitMQ(JObject config, WebApplicationBuilder builder)
	{
		var rabbitSettings = config["RabbitMqSettings"];
		if (rabbitSettings != null)
		{
			builder.Configuration["RabbitMqSettings:InstanceNetworkGateId"] = rabbitSettings["InstanceNetworkGateId"]?.ToString() ?? "";
			builder.Configuration["RabbitMqSettings:TypeToRun"] = rabbitSettings["TypeToRun"]?.ToString() ?? "RabbitMQ";
			builder.Configuration["RabbitMqSettings:HostName"] = rabbitSettings["HostName"]?.ToString() ?? "localhost";
			builder.Configuration["RabbitMqSettings:Port"] = rabbitSettings["Port"]?.ToString() ?? "5672";
			builder.Configuration["RabbitMqSettings:UserName"] = rabbitSettings["UserName"]?.ToString() ?? "guest";
			builder.Configuration["RabbitMqSettings:Password"] = rabbitSettings["Password"]?.ToString() ?? "guest";
			builder.Configuration["RabbitMqSettings:VirtualHost"] = rabbitSettings["VirtualHost"]?.ToString() ?? "/";
			builder.Configuration["RabbitMqSettings:PushQueueName"] = rabbitSettings["PushQueueName"]?.ToString() ?? "";
			builder.Configuration["RabbitMqSettings:ListenQueueName"] = rabbitSettings["ListenQueueName"]?.ToString() ?? "";
			builder.Configuration["RabbitMqSettings:Heartbeat"] = rabbitSettings["Heartbeat"]?.ToString() ?? "60";
		}
	}

	private static void ConfigureActiveMQ(JObject config, WebApplicationBuilder builder)
	{
		var activeMqSettings = config["ActiveMqSettings"];
		if (activeMqSettings != null)
		{
			builder.Configuration["ActiveMqSettings:InstanceNetworkGateId"] = activeMqSettings["InstanceNetworkGateId"]?.ToString() ?? "";
			builder.Configuration["ActiveMqSettings:TypeToRun"] = activeMqSettings["TypeToRun"]?.ToString() ?? "ActiveMQ";
			builder.Configuration["ActiveMqSettings:BrokerUri"] = activeMqSettings["BrokerUri"]?.ToString() ?? "";
			builder.Configuration["ActiveMqSettings:QueueName"] = activeMqSettings["QueueName"]?.ToString() ?? "";
		}
	}

	private static void ConfigureKafka(JObject config, WebApplicationBuilder builder)
	{
		var kafkaSettings = config["KafkaStreamsSettings"];
		if (kafkaSettings != null)
		{
			builder.Configuration["KafkaStreamsSettings:InstanceNetworkGateId"] = kafkaSettings["InstanceNetworkGateId"]?.ToString() ?? "";
			builder.Configuration["KafkaStreamsSettings:TypeToRun"] = kafkaSettings["TypeToRun"]?.ToString() ?? "Kafka";
			builder.Configuration["KafkaStreamsSettings:BootstrapServers"] = kafkaSettings["BootstrapServers"]?.ToString() ?? "localhost:9092";
			builder.Configuration["KafkaStreamsSettings:ApplicationId"] = kafkaSettings["ApplicationId"]?.ToString() ?? "";
			builder.Configuration["KafkaStreamsSettings:InputTopic"] = kafkaSettings["InputTopic"]?.ToString() ?? "";
			builder.Configuration["KafkaStreamsSettings:OutputTopic"] = kafkaSettings["OutputTopic"]?.ToString() ?? "";
		}
	}

	private static void LogDetailedConfiguration(string companyName, string host, int port, bool enableValidation,
		string database, string bus, int cleanupIntervalSeconds, int outboxMessageTtlSeconds, int incidentEntitiesTtlMonths,
		JObject config)
	{
		var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");

		// Используем цвета для выделения важной информации
		Console.ForegroundColor = ConsoleColor.Cyan;
		Console.WriteLine();
		Console.WriteLine("═══════════════════════════════════════════════════════════════");
		Console.WriteLine("                    КОНФИГУРАЦИЯ ШЛЮЗА                        ");
		Console.WriteLine("═══════════════════════════════════════════════════════════════");
		Console.ResetColor();

		Console.ForegroundColor = ConsoleColor.Gray;
		Console.WriteLine($"Время загрузки:        {timestamp}");
		Console.ResetColor();

		Console.ForegroundColor = ConsoleColor.White;
		Console.WriteLine($"Компания:              {companyName}");
		Console.WriteLine($"Хост:                  {host}:{port}");
		Console.ResetColor();

		Console.ForegroundColor = ConsoleColor.Yellow;
		Console.WriteLine($"База данных:           {database.ToUpper()}");
		Console.WriteLine($"Шина сообщений:        {bus.ToUpper()}");
		Console.ResetColor();

		Console.ForegroundColor = ConsoleColor.Green;
		Console.WriteLine($"Валидация:             {(enableValidation ? "Включена" : "Отключена")}");
		Console.ResetColor();

		Console.ForegroundColor = ConsoleColor.Magenta;
		Console.WriteLine($"Интервал очистки:      {cleanupIntervalSeconds} сек");
		Console.WriteLine($"TTL сообщений:         {outboxMessageTtlSeconds} сек");
		Console.WriteLine($"TTL инцидентов:        {incidentEntitiesTtlMonths} мес");
		Console.ResetColor();

		// Подробности базы данных
		if (database.ToLower() == "postgres")
		{
			var pgSettings = config["PostgresDbSettings"];
			if (pgSettings != null)
			{
				Console.ForegroundColor = ConsoleColor.Blue;
				Console.WriteLine($"PostgreSQL:            {pgSettings["Host"]}:{pgSettings["Port"]}/{pgSettings["Database"]}");
				Console.WriteLine($"Пользователь:          {pgSettings["Username"]}");
				Console.ResetColor();
			}
		}
		else if (database.ToLower() == "mongo")
		{
			var mongoSettings = config["MongoDbSettings"];
			if (mongoSettings != null)
			{
				Console.ForegroundColor = ConsoleColor.Blue;
				Console.WriteLine($"MongoDB:               {mongoSettings["DatabaseName"]}");
				var host_mongo = ExtractHostFromConnectionString(mongoSettings["ConnectionString"]?.ToString());
				if (!string.IsNullOrEmpty(host_mongo))
				{
					Console.WriteLine($"MongoDB Host:          {host_mongo}");
				}
				Console.ResetColor();
			}
		}

		// Подробности шины сообщений
		if (bus.ToLower() == "rabbit")
		{
			var rabbitSettings = config["RabbitMqSettings"];
			if (rabbitSettings != null)
			{
				Console.ForegroundColor = ConsoleColor.DarkYellow;
				Console.WriteLine($"RabbitMQ:              {rabbitSettings["HostName"]}:{rabbitSettings["Port"]}");
				Console.WriteLine($"Push -> Listen:        {rabbitSettings["PushQueueName"]} -> {rabbitSettings["ListenQueueName"]}");
				Console.WriteLine($"Virtual Host:          {rabbitSettings["VirtualHost"]}");
				Console.WriteLine($"Gate ID:               {rabbitSettings["InstanceNetworkGateId"]}");
				Console.ResetColor();
			}
		}
		else if (bus.ToLower() == "activemq")
		{
			var activeMqSettings = config["ActiveMqSettings"];
			if (activeMqSettings != null)
			{
				Console.ForegroundColor = ConsoleColor.DarkYellow;
				Console.WriteLine($"ActiveMQ:              {activeMqSettings["BrokerUri"]}");
				Console.WriteLine($"Очередь:               {activeMqSettings["QueueName"]}");
				Console.WriteLine($"Gate ID:               {activeMqSettings["InstanceNetworkGateId"]}");
				Console.ResetColor();
			}
		}
		else if (bus.ToLower() == "kafka")
		{
			var kafkaSettings = config["KafkaStreamsSettings"];
			if (kafkaSettings != null)
			{
				Console.ForegroundColor = ConsoleColor.DarkYellow;
				Console.WriteLine($"Kafka:                 {kafkaSettings["BootstrapServers"]}");
				Console.WriteLine($"Input -> Output:       {kafkaSettings["InputTopic"]} -> {kafkaSettings["OutputTopic"]}");
				Console.WriteLine($"Application ID:        {kafkaSettings["ApplicationId"]}");
				Console.WriteLine($"Gate ID:               {kafkaSettings["InstanceNetworkGateId"]}");
				Console.ResetColor();
			}
		}

		Console.ForegroundColor = ConsoleColor.Cyan;
		Console.WriteLine("═══════════════════════════════════════════════════════════════");
		Console.ResetColor();
		Console.WriteLine();

		// Дополнительная основная информация о настройках с цветом
		Console.ForegroundColor = ConsoleColor.Green;
		Console.WriteLine($"[{timestamp}] [INFO] Конфигурация динамического шлюза загружена:");
		Console.ResetColor();

		Console.WriteLine($"[{timestamp}] [INFO] - Company: {companyName}");
		Console.WriteLine($"[{timestamp}] [INFO] - Host: {host}:{port}");
		Console.WriteLine($"[{timestamp}] [INFO] - Database: {database}");
		Console.WriteLine($"[{timestamp}] [INFO] - Bus: {bus}");
		Console.WriteLine($"[{timestamp}] [INFO] - Validation: {enableValidation}");
		Console.WriteLine($"[{timestamp}] [INFO] - Cleanup Interval: {cleanupIntervalSeconds} seconds");
		Console.WriteLine($"[{timestamp}] [INFO] - Outbox TTL: {outboxMessageTtlSeconds} seconds");
		Console.WriteLine($"[{timestamp}] [INFO] - Incidents TTL: {incidentEntitiesTtlMonths} months\n");
	}

	private static string ExtractHostFromConnectionString(string connectionString)
	{
		if (string.IsNullOrEmpty(connectionString))
			return "";

		try
		{
			// Простое извлечение хоста из MongoDB connection string
			var uri = new Uri(connectionString);
			return $"{uri.Host}:{uri.Port}";
		}
		catch
		{
			return "";
		}
	}

	private static JObject LoadConfiguration(string configFilePath)
	{
		try
		{
			var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
			string fullPath;

			if (Path.IsPathRooted(configFilePath))
			{
				fullPath = configFilePath;
			}
			else
			{
				var basePath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), ".."));
				fullPath = Path.GetFullPath(Path.Combine(basePath, configFilePath));
			}

			// Печатаем информацию для отладки.
			Console.WriteLine();
			Console.WriteLine($"[{timestamp}] [INFO] Конечный путь к конфигу: {fullPath}");
			Console.WriteLine($"[{timestamp}] [INFO] Загружается конфигурация: {Path.GetFileName(fullPath)}");
			Console.WriteLine();

			// Проверка существования файла.
			if (!File.Exists(fullPath))
				throw new FileNotFoundException("Файл конфигурации не найден", fullPath);

			// Загружаем конфигурацию из файла.
			var json = File.ReadAllText(fullPath);
			var parsedConfig = JObject.Parse(json);

			var endTimestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
			Console.WriteLine($"[{endTimestamp}] [INFO] ✓ Конфигурация успешно загружена из {Path.GetFileName(fullPath)}");
			return parsedConfig;
		}
		catch (Exception ex)
		{
			var errorTimestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
			throw new InvalidOperationException($"[{errorTimestamp}] Ошибка при загрузке конфигурации из файла '{configFilePath}': {ex.Message}");
		}
	}
}

/// <summary>
/// Модель конфигурации шлюза с валидацией
/// </summary>
public class GateConfigurationModel
{
	[Required]
	public string Type { get; set; } = "rest";

	[Required]
	public string CompanyName { get; set; } = "";

	[Required]
	public string Host { get; set; } = "localhost";

	[Range(1, 65535)]
	public int Port { get; set; } = 5000;

	public bool Validate { get; set; } = true;

	[Required]
	[RegularExpression("^(postgres|mongo)$", ErrorMessage = "Database должен быть 'postgres' или 'mongo'")]
	public string Database { get; set; } = "mongo";

	[Required]
	[RegularExpression("^(rabbit|activemq|kafka)$", ErrorMessage = "Bus должен быть 'rabbit', 'activemq' или 'kafka'")]
	public string Bus { get; set; } = "rabbit";

	[Range(1, 3600)]
	public int CleanupIntervalSeconds { get; set; } = 5;

	[Range(1, 86400)]
	public int OutboxMessageTtlSeconds { get; set; } = 10;

	[Range(1, 120)]
	public int IncidentEntitiesTtlMonths { get; set; } = 60;

	public PostgresDbSettings? PostgresDbSettings { get; set; }
	public MongoDbSettings MongoDbSettings { get; set; }
	public RabbitMqSettings RabbitMqSettings { get; set; }
	public ActiveMqSettings ActiveMqSettings { get; set; }
	public KafkaStreamsSettings KafkaStreamsSettings { get; set; }
}

// Модели настроек - все в одном namespace
public class MongoDbSettings
{
	[Required]
	public string ConnectionString { get; set; } = "";

	[Required]
	public string DatabaseName { get; set; } = "";

	// Добавляем поля User и Password
	public string User { get; set; } = "";

	public string Password { get; set; } = "";

	[Required]
	public MongoDbCollections Collections { get; set; } = new();
}

public class MongoDbCollections
{
	[Required]
	public string QueueCollection { get; set; } = "QueueEntities";

	[Required]
	public string OutboxCollection { get; set; } = "OutboxMessages";

	[Required]
	public string IncidentCollection { get; set; } = "IncidentEntities";
}

public class PostgresDbSettings
{
	[Required]
	public string Host { get; set; } = "localhost";

	[Range(1, 65535)]
	public int Port { get; set; } = 5432;

	[Required]
	public string Username { get; set; } = "postgres";

	[Required]
	public string Password { get; set; } = "";

	[Required]
	public string Database { get; set; } = "GatewayDB";

	public string GetConnectionString()
	{
		return $"Host={Host};Port={Port};Username={Username};Password={Password};Database={Database}";
	}
}

public enum MessageBusType
{
	RabbitMQ = 0,
	ActiveMQ = 1,
	Kafka = 2
}

public abstract class MessageBusBaseSettings
{
	public string InstanceNetworkGateId { get; set; } = string.Empty;
	public MessageBusType TypeToRun { get; set; }
}

public class RabbitMqSettings : MessageBusBaseSettings
{
	[Required]
	public string HostName { get; set; } = "";

	[Range(1, 65535)]
	public int Port { get; set; } = 5672;

	[Required]
	public string UserName { get; set; } = "";

	[Required]
	public string Password { get; set; } = "";

	[Required]
	public string VirtualHost { get; set; } = "";

	public string Heartbeat { get; set; } = "60";

	[Required]
	public string PushQueueName { get; set; } = "";

	[Required]
	public string ListenQueueName { get; set; } = "";

	public Uri GetAmqpUri()
	{
		var vhost = string.IsNullOrWhiteSpace(VirtualHost) ? "/" : VirtualHost.TrimStart('/');
		var uriString = $"amqp://{UserName}:{Password}@{HostName}/{vhost}";
		return new Uri(uriString);
	}
}

public class ActiveMqSettings : MessageBusBaseSettings
{
	[Required]
	public string BrokerUri { get; set; } = string.Empty;

	[Required]
	public string QueueName { get; set; } = string.Empty;
}

public class KafkaStreamsSettings : MessageBusBaseSettings
{
	[Required]
	public string BootstrapServers { get; set; } = "";

	[Required]
	public string ApplicationId { get; set; } = "";

	[Required]
	public string InputTopic { get; set; } = "";

	[Required]
	public string OutputTopic { get; set; } = "";
}