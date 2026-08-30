using Helpdesk.Api.Models;

namespace Helpdesk.Api.Services;

public class AutoCloseBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AutoCloseBackgroundService> _logger;

    public AutoCloseBackgroundService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<AutoCloseBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var timeoutHours = _configuration.GetValue("Helpdesk:AutoCloseTimeoutHours", 72);
        var timeout = TimeSpan.FromHours(timeoutHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<TicketService>();
                var closed = await service.AutoCloseAsync(timeout, DateTime.UtcNow);
                if (closed > 0)
                {
                    _logger.LogInformation("Auto-closed {Count} resolved ticket(s) with no response.", closed);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while auto-closing resolved tickets.");
            }

            await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
        }
    }
}
