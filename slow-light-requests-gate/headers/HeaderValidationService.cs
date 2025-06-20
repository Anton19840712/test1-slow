using lazy_light_requests_gate.headers;

public class HeaderValidationService : IHeaderValidationService
{
	private readonly IHeadersValidator _simpleValidator;
	private readonly IHeadersValidator _detailedValidator;
	private readonly ILogger<HeaderValidationService> _logger;

	public HeaderValidationService(
		SimpleHeadersValidator simpleValidator,
		DetailedHeadersValidator detailedValidator,
		ILogger<HeaderValidationService> logger)
	{
		_simpleValidator = simpleValidator ?? throw new ArgumentNullException(nameof(simpleValidator));
		_detailedValidator = detailedValidator ?? throw new ArgumentNullException(nameof(detailedValidator));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
	}

	public async Task<bool> ValidateHeadersAsync(IHeaderDictionary headers)
	{
		// если в заголовках в наличии ключ X-Use-Detailed-Validation установлен в true - тогда дальнейшая работа сервера будет происходить по детальному сценарию валидации для ответов:
		bool useDetailedValidation = headers.ContainsKey("X-Use-Detailed-Validation");
		IHeadersValidator validator = useDetailedValidation ? _detailedValidator : _simpleValidator;

		// Логируем тип используемого валидатора
		_logger.LogInformation("Для валидации было использован валидатор вида: {ValidatorType}", validator.GetType().FullName);

		var validationResult = await validator.ValidateHeadersAsync(headers);

		if (!validationResult.Result)
		{
			_logger.LogWarning("Валидация заголовков не пройдена: {Message}", validationResult.Message);
			return false;
		}

		_logger.LogInformation("Валидация заголовков успешна.");
		return true;
	}
}
