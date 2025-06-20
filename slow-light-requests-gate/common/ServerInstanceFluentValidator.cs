using FluentValidation;
using FluentValidation.Results;
using lazy_light_requests_gate.common;
using lazy_light_requests_gate.models;
using lazy_light_requests_gate.response;

public class ServerInstanceFluentValidator : IServerInstanceFluentValidator
{
	private readonly IValidator<ServerInstanceModel> _validator;
	private readonly ILogger<ServerInstanceFluentValidator> _logger;

	public ServerInstanceFluentValidator(
		IValidator<ServerInstanceModel> validator,
		ILogger<ServerInstanceFluentValidator> logger)
	{
		_validator = validator;
		_logger = logger;
	}

	public ResponseIntegration Validate(ServerInstanceModel instanceModel)
	{
		ValidationResult result = _validator.Validate(instanceModel);

		if (!result.IsValid)
		{
			foreach (var error in result.Errors)
			{
				_logger.LogError(error.ErrorMessage);
			}

			return new ResponseIntegration
			{
				Message = string.Join("; ", result.Errors.Select(e => e.ErrorMessage)),
				Result = false
			};
		}

		return new ResponseIntegration
		{
			Message = "No mistakes found",
			Result = true
		};
	}
}
