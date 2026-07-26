using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using ABCPClient.Application.DTO;
using ABCPClient.Application.Interfaces;
using ABCPClient.Contracts;
using ABCPClient.Domain.Entities;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ABCPClient.Hub;

/// <summary>
/// Узел склада: принимает обращения терминалов сборки по локальной сети.
/// </summary>
/// <remarks>
/// Живёт внутри настольной программы, а не отдельной службой: программа и так
/// запущена на складе, держит локальную базу и синхронизируется с ABCP.
/// Заводить второй процесс с той же базой значило бы делить её между
/// приложениями без нужды.
/// Своего контейнера служб узел не создаёт: нужные ему службы передаются
/// готовыми экземплярами из контейнера программы, поэтому база, кэш карточек
/// и задания у них общие.
/// </remarks>
public sealed class WarehouseHub : IHostedService, IAsyncDisposable
{
    private readonly IServiceProvider _services;
    private readonly IOptionsMonitor<HubOptions> _options;
    private readonly ILogger<WarehouseHub> _logger;

    private WebApplication? _application;

    /// <summary>Создаёт узел.</summary>
    public WarehouseHub(
        IServiceProvider services,
        IOptionsMonitor<HubOptions> options,
        ILogger<WarehouseHub> logger)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _services = services;
        _options = options;
        _logger = logger;
    }

    /// <summary>Узел слушает обращения.</summary>
    public bool IsRunning => _application is not null;

    /// <summary>Порт, на котором узел слушает.</summary>
    public int Port => _options.CurrentValue.Port;

    /// <summary>
    /// Адреса, которые нужно набрать на терминале.
    /// </summary>
    /// <remarks>
    /// Перечисляются адреса всех работающих сетевых интерфейсов: на складе
    /// компьютер обычно и в проводной сети, и в Wi-Fi, и угадать за пользователя,
    /// какой из адресов увидит терминал, нельзя.
    /// </remarks>
    public IReadOnlyList<string> GetAddresses()
    {
        int port = Port;

        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up
                && adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
            .Select(address => address.Address)
            .Where(address => address.AddressFamily == AddressFamily.InterNetwork && IsPrivate(address))
            .Select(address => $"http://{address}:{port}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        HubOptions options = _options.CurrentValue;

        if (!options.Enabled)
        {
            _logger.LogInformation("Узел склада выключен в настройках");
            return;
        }

        try
        {
            _application = Build(options);
            await _application.StartAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Узел склада слушает порт {Port}. Адреса для терминала: {Addresses}",
                options.Port,
                string.Join(", ", GetAddresses()));
        }
        catch (Exception exception)
        {
            // Программа должна работать и без узла: заказы ведут на компьютере,
            // а терминал — дополнение. Порт мог быть занят или закрыт брандмауэром.
            _logger.LogError(
                exception,
                "Не удалось запустить узел склада на порту {Port}. Терминалы не смогут подключиться",
                options.Port);

            _application = null;
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_application is null)
        {
            return;
        }

        await _application.StopAsync(cancellationToken).ConfigureAwait(false);
        await _application.DisposeAsync().ConfigureAwait(false);

        _application = null;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await StopAsync(CancellationToken.None).ConfigureAwait(false);

    private WebApplication Build(HubOptions options)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();

        // Слушаются все интерфейсы: терминал подключается по адресу компьютера
        // в сети склада, а не по localhost.
        builder.Services.Configure<KestrelServerOptions>(kestrel => kestrel.ListenAnyIP(options.Port));

        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(_services.GetRequiredService<ILoggerFactory>());

        // Службы передаются готовыми экземплярами из контейнера программы:
        // база, кэш карточек и задания должны быть теми же самыми.
        builder.Services.AddSingleton(_services.GetRequiredService<IPickingService>());
        builder.Services.AddSingleton(_services.GetRequiredService<IArticleLookup>());
        builder.Services.AddSingleton(_services.GetRequiredService<IArticleCardRepository>());
        builder.Services.AddSingleton(_services.GetRequiredService<IProductImageCache>());
        builder.Services.AddSingleton(_services.GetRequiredService<DeviceRegistry>());
        builder.Services.AddSingleton(_options);

        WebApplication application = builder.Build();

        application.Use(RestrictToLocalNetworkAsync);
        application.Use(AuthenticateDeviceAsync);

        MapEndpoints(application);

        return application;
    }

    /// <summary>
    /// Отклоняет обращения не из локальной сети.
    /// </summary>
    /// <remarks>
    /// Узел отдаёт состав заказов и принимает отметки о сборке, шифрования у него
    /// нет. Если компьютер окажется доступен из интернета, это ограничение удержит
    /// посторонних даже при утёкшем токене.
    /// </remarks>
    private async Task RestrictToLocalNetworkAsync(HttpContext context, RequestDelegate next)
    {
        if (_options.CurrentValue.AllowRemoteNetworks)
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        IPAddress? remote = context.Connection.RemoteIpAddress;

        if (remote is null || !(IPAddress.IsLoopback(remote) || IsPrivate(remote)))
        {
            _logger.LogWarning("Отклонено обращение к узлу с адреса {Address}", remote);

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response
                .WriteAsJsonAsync(new HubError("Узел доступен только из локальной сети склада"))
                .ConfigureAwait(false);

            return;
        }

        await next(context).ConfigureAwait(false);
    }

    /// <summary>
    /// Проверяет токен устройства.
    /// </summary>
    /// <remarks>
    /// Без токена доступны только проверка связи и подключение по коду: терминалу
    /// нужно убедиться, что адрес верный, до того как он получит токен.
    /// </remarks>
    private static async Task AuthenticateDeviceAsync(HttpContext context, RequestDelegate next)
    {
        string path = context.Request.Path.Value ?? string.Empty;

        if (path.StartsWith("/api/health", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/devices/pair", StringComparison.OrdinalIgnoreCase))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        string? token = context.Request.Headers.Authorization.ToString() is { Length: > 7 } header
            && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? header[7..]
                : null;

        DeviceRegistry devices = context.RequestServices.GetRequiredService<DeviceRegistry>();
        string? device = await devices.ResolveDeviceAsync(token, context.RequestAborted).ConfigureAwait(false);

        if (device is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response
                .WriteAsJsonAsync(new HubError("Устройство не подключено. Выполните подключение по коду"))
                .ConfigureAwait(false);

            return;
        }

        context.Items[DeviceNameKey] = device;

        await next(context).ConfigureAwait(false);
    }

    /// <summary>Ключ, под которым имя устройства лежит в данных запроса.</summary>
    internal const string DeviceNameKey = "hub.device";

    private static void MapEndpoints(WebApplication application)
    {
        application.MapGet("/api/health", () => Results.Ok(new HubError("ok")));

        application.MapPost("/api/devices/pair", async (
            DeviceAuthRequest request,
            DeviceRegistry devices,
            CancellationToken cancellationToken) =>
        {
            string? token = await devices
                .TryPairAsync(request.PairingCode, request.DeviceName, cancellationToken)
                .ConfigureAwait(false);

            return token is null
                ? Results.Json(
                    new HubError("Код неверен или истёк. Получите новый в программе на компьютере"),
                    statusCode: StatusCodes.Status401Unauthorized)
                : Results.Ok(new DeviceAuthResponse(token, request.DeviceName));
        });

        application.MapGet("/api/hub/info", async (
            IPickingService picking,
            CancellationToken cancellationToken) =>
        {
            IReadOnlyList<PickingTaskListItem> open = await picking
                .GetTasksAsync(new PickingTaskFilter { OnlyOpen = true }, cancellationToken)
                .ConfigureAwait(false);

            string version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "0.0.0";

            return Results.Ok(new HubInfo("ABCP Client", version, DateTimeOffset.Now, open.Count));
        });

        application.MapGet("/api/picking/tasks", async (
            bool? onlyOpen,
            string? search,
            IPickingService picking,
            CancellationToken cancellationToken) =>
        {
            IReadOnlyList<PickingTaskListItem> tasks = await picking
                .GetTasksAsync(
                    new PickingTaskFilter { OnlyOpen = onlyOpen ?? true, SearchText = search },
                    cancellationToken)
                .ConfigureAwait(false);

            return Results.Ok(tasks.Select(HubMapping.ToSummary).ToArray());
        });

        application.MapGet("/api/picking/tasks/{id:int}", async (
            int id,
            IPickingService picking,
            IArticleCardRepository cards,
            CancellationToken cancellationToken) =>
        {
            PickingTask? task = await picking.GetTaskAsync(id, cancellationToken).ConfigureAwait(false);
            if (task is null)
            {
                return Results.NotFound(new HubError($"Задание {id} не найдено"));
            }

            // Изображения к строкам подтягиваются из кэша карточек: к API узел
            // не обращается, его лимит терминалом расходовать нельзя.
            IReadOnlyDictionary<string, ArticleCard> found = await cards
                .GetAsync(
                    task.Lines.Select(line => new ArticleRef(line.Brand, line.Number)).ToArray(),
                    cancellationToken)
                .ConfigureAwait(false);

            return Results.Ok(HubMapping.ToDetails(task, found));
        });

        application.MapPost("/api/picking/tasks/{id:int}/lines/{lineId:int}/pick", async (
            int id,
            int lineId,
            PickLineRequest request,
            HttpContext context,
            IPickingService picking,
            IArticleCardRepository cards,
            CancellationToken cancellationToken) =>
        {
            try
            {
                PickingTask task = await picking
                    .RegisterPickAsync(
                        new PickRequest(id, lineId, request.Quantity, DeviceName(context)),
                        cancellationToken)
                    .ConfigureAwait(false);

                IReadOnlyDictionary<string, ArticleCard> found = await cards
                    .GetAsync(
                        task.Lines.Select(line => new ArticleRef(line.Brand, line.Number)).ToArray(),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(HubMapping.ToDetails(task, found));
            }
            catch (InvalidOperationException exception)
            {
                // Задание отменено, закрыто или строка чужая — это ошибка запроса,
                // а не сбой узла, и терминал должен показать её сборщику.
                return Results.BadRequest(new HubError(exception.Message));
            }
        });

        application.MapPost("/api/picking/tasks/{id:int}/complete", async (
            int id,
            CompleteTaskRequest? request,
            HttpContext context,
            IPickingService picking,
            IArticleCardRepository cards,
            CancellationToken cancellationToken) =>
        {
            try
            {
                PickingTask task = await picking
                    .CompleteTaskAsync(id, DeviceName(context), cancellationToken)
                    .ConfigureAwait(false);

                IReadOnlyDictionary<string, ArticleCard> found = await cards
                    .GetAsync(
                        task.Lines.Select(line => new ArticleRef(line.Brand, line.Number)).ToArray(),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(HubMapping.ToDetails(task, found));
            }
            catch (InvalidOperationException exception)
            {
                return Results.BadRequest(new HubError(exception.Message));
            }
        });

        application.MapGet("/api/articles/lookup", async (
            string? query,
            IArticleLookup lookup,
            CancellationToken cancellationToken) =>
        {
            ArticleLookupResult result = await lookup
                .LookupAsync(query ?? string.Empty, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return Results.Ok(new ArticleLookupResponse(
                HubMapping.ToCode(result.Kind),
                result.Input,
                result.Matches.Select(HubMapping.ToMatch).ToArray()));
        });

        application.MapGet("/api/images", async (
            string? name,
            IProductImageCache images,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return Results.BadRequest(new HubError("Не указано изображение"));
            }

            // Имя передаётся строкой запроса, а не частью пути: в выгрузке каталога
            // это полный адрес с косыми чертами, и в путь он не укладывается.
            string? path = await images.GetOrDownloadAsync(name, cancellationToken).ConfigureAwait(false);

            return path is null
                ? Results.NotFound(new HubError("Изображение недоступно"))
                : Results.File(path, "image/jpeg");
        });
    }

    private static string? DeviceName(HttpContext context) =>
        context.Items.TryGetValue(DeviceNameKey, out object? value) ? value as string : null;

    /// <summary>
    /// Относится ли адрес к частным диапазонам.
    /// </summary>
    internal static bool IsPrivate(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || IPAddress.IsLoopback(address);
        }

        byte[] octets = address.GetAddressBytes();

        return octets[0] switch
        {
            10 => true,
            127 => true,
            172 => octets[1] >= 16 && octets[1] <= 31,
            192 => octets[1] == 168,

            // Адрес, назначенный при отсутствии DHCP: сеть всё равно локальная.
            169 => octets[1] == 254,
            _ => false,
        };
    }
}
