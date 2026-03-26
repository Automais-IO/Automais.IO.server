using Automais.Core;
using Automais.Core.Configuration;
using Automais.Core.Entities;
using Automais.Core.Interfaces;
using Automais.Core.Services;
using Automais.Infrastructure.ChirpStack;
using Automais.Infrastructure.Data;
using Automais.Infrastructure.Repositories;
using Automais.Infrastructure.RouterOS;
using Automais.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography.X509Certificates;
using System.IO;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Habilitar integração com systemd (notifica quando a aplicação está pronta)
builder.Host.UseSystemd();

// ===== Configuração de Serviços =====

// Substituir variáveis de ambiente no formato ${VAR} nas configurações
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' não encontrada.");

Console.WriteLine($"🔍 Connection string original: {MaskConnectionString(connectionString)}");

// Verificar quais variáveis foram encontradas ANTES da substituição
var envVars = new[] { "DB_HOST", "DB_PORT", "DB_NAME", "DB_USER", "DB_PASSWORD" };
Console.WriteLine("🔍 Verificando variáveis de ambiente:");
var missingVars = new List<string>();
foreach (var varName in envVars)
{
    var value = Environment.GetEnvironmentVariable(varName);
    if (string.IsNullOrEmpty(value))
    {
        Console.WriteLine($"  ❌ {varName}: NÃO DEFINIDA");
        missingVars.Add(varName);
    }
    else
    {
        Console.WriteLine($"  ✅ {varName}: {(varName.Contains("PASSWORD") ? "***" : value)}");
    }
}

// Se todas as variáveis estão definidas, fazer a substituição
// Caso contrário, tentar construir a connection string diretamente
string baseConnectionString;
if (missingVars.Any())
{
    Console.WriteLine($"⚠️ Variáveis faltando: {string.Join(", ", missingVars)}");
    Console.WriteLine("🔧 Tentando construir connection string diretamente das variáveis...");
    
    // Tentar construir a connection string diretamente
    var host = Environment.GetEnvironmentVariable("DB_HOST") ?? "";
    var port = Environment.GetEnvironmentVariable("DB_PORT") ?? "5432";
    var database = Environment.GetEnvironmentVariable("DB_NAME") ?? "";
    var username = Environment.GetEnvironmentVariable("DB_USER") ?? "";
    var password = Environment.GetEnvironmentVariable("DB_PASSWORD") ?? "";
    
    if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(database) || 
        string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
    {
        throw new InvalidOperationException(
            $"Não foi possível construir a connection string. Variáveis faltando: {string.Join(", ", missingVars)}. " +
            $"Verifique se as variáveis estão configuradas no systemd service.");
    }
    
    baseConnectionString = $"Host={host};Port={port};Database={database};Username={username};Password={password};Ssl Mode=Require";
    Console.WriteLine($"✅ Connection string construída diretamente: {MaskConnectionString(baseConnectionString)}");
}
else
{
    // Substituir variáveis de ambiente no formato ${VAR}
    baseConnectionString = ReplaceEnvironmentVariables(connectionString);
    Console.WriteLine($"✅ Connection string após substituição: {MaskConnectionString(baseConnectionString)}");
}

// Validar se a connection string tem host
if (string.IsNullOrWhiteSpace(baseConnectionString))
{
    throw new InvalidOperationException("Connection string está vazia após substituição de variáveis de ambiente.");
}

// Verificar se a connection string tem Host
if (!baseConnectionString.Contains("Host=", StringComparison.OrdinalIgnoreCase) && 
    !baseConnectionString.Contains("Server=", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException("Connection string não contém Host ou Server. Verifique a configuração.");
}

var rootCertSetting = builder.Configuration["Database:RootCertificatePath"];

// Validar e construir connection string
NpgsqlConnectionStringBuilder npgBuilder;
try
{
    npgBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString)
    {
        SslMode = SslMode.Require,
        TrustServerCertificate = true,
        CommandTimeout = 30, // Timeout para comandos SQL (30 segundos - reduzido de 60)
        Timeout = 15, // Timeout para estabelecer conexão (15 segundos - reduzido de 30)
        ConnectionIdleLifetime = 180, // Fechar conexões idle após 3 minutos (reduzido de 5)
        ConnectionPruningInterval = 5, // Verificar conexões idle a cada 5 segundos (reduzido de 10)
        MaxPoolSize = 50, // Máximo de conexões no pool (reduzido de 100 para evitar esgotamento)
        MinPoolSize = 2 // Mínimo de conexões no pool (reduzido de 5)
    };

    // Validar se o Host foi configurado
    if (string.IsNullOrWhiteSpace(npgBuilder.Host))
    {
        throw new InvalidOperationException(
            "Connection string não contém Host. " +
            "Verifique se a variável de ambiente está configurada corretamente. " +
            $"Connection string (parcial): {MaskConnectionString(baseConnectionString)}");
    }
}
catch (ArgumentException ex)
{
    throw new InvalidOperationException(
        $"Erro ao processar connection string: {ex.Message}. " +
        $"Verifique se a connection string está no formato correto. " +
        $"Connection string (parcial): {MaskConnectionString(baseConnectionString)}", ex);
}

string? finalCertPath = null;

// Tentar primeiro o caminho configurado
if (!string.IsNullOrWhiteSpace(rootCertSetting))
{
    var rootCertPath = Path.IsPathRooted(rootCertSetting)
        ? rootCertSetting
        : Path.Combine(builder.Environment.ContentRootPath, rootCertSetting);

    if (File.Exists(rootCertPath))
    {
        finalCertPath = rootCertPath;
    }
}

// Se não encontrou, tentar no diretório pai (fixo no servidor)
if (string.IsNullOrEmpty(finalCertPath))
{
    var parentDirCertPath = Path.Combine(
        Path.GetDirectoryName(builder.Environment.ContentRootPath) ?? string.Empty,
        "ca-certificate.crt");
    
    if (File.Exists(parentDirCertPath))
    {
        finalCertPath = parentDirCertPath;
        Console.WriteLine($"🔍 Certificado encontrado no diretório pai: {finalCertPath}");
    }
}

// Se ainda não encontrou, tentar caminho absoluto fixo (Linux)
if (string.IsNullOrEmpty(finalCertPath))
{
    var fixedPath = "/root/automais.io/ca-certificate.crt";
    if (File.Exists(fixedPath))
    {
        finalCertPath = fixedPath;
        Console.WriteLine($"🔍 Certificado encontrado no caminho fixo: {finalCertPath}");
    }
}

// Aplicar certificado se encontrado
if (!string.IsNullOrEmpty(finalCertPath))
{
    Console.WriteLine($"🔐 Certificado raiz encontrado em {finalCertPath}. Validando SSL.");
    npgBuilder.RootCertificate = finalCertPath;
    npgBuilder.TrustServerCertificate = false;
    npgBuilder.SslMode = SslMode.VerifyFull;
}
else
{
    Console.WriteLine($"⚠️ Certificado raiz não encontrado em nenhum local. Usando TrustServerCertificate=true.");
    Console.WriteLine($"⚠️ Locais verificados:");
    if (!string.IsNullOrWhiteSpace(rootCertSetting))
    {
        var rootCertPath = Path.IsPathRooted(rootCertSetting)
            ? rootCertSetting
            : Path.Combine(builder.Environment.ContentRootPath, rootCertSetting);
        Console.WriteLine($"   - {rootCertPath}");
    }
    var parentDirCertPath = Path.Combine(
        Path.GetDirectoryName(builder.Environment.ContentRootPath) ?? string.Empty,
        "ca-certificate.crt");
    Console.WriteLine($"   - {parentDirCertPath}");
    Console.WriteLine($"   - /root/automais.io/ca-certificate.crt");
}

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseNpgsql(npgBuilder.ConnectionString, opt =>
    {
        opt.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(30),
            errorCodesToAdd: null);
        opt.CommandTimeout(60); // Timeout adicional para comandos EF Core
    });
    // REMOVIDO: UseSnakeCaseNamingConvention() 
    // O banco usa PascalCase (Id, Name, TenantId), não snake_case
    // options.UseSnakeCaseNamingConvention();
});

// ChirpStack Client (gRPC)
var chirpStackConfig = builder.Configuration.GetSection("ChirpStack");
var chirpStackUrl = ReplaceEnvironmentVariables(chirpStackConfig["ApiUrl"] ?? "http://srv01.automais.io:8080");
var chirpStackToken = ReplaceEnvironmentVariables(chirpStackConfig["ApiToken"] ?? "");

// Validar URL do ChirpStack
if (string.IsNullOrWhiteSpace(chirpStackUrl))
{
    Console.WriteLine("⚠️ ChirpStack URL não configurada. Algumas funcionalidades podem não funcionar.");
}
else
{
    // Validar formato da URL
    if (!Uri.TryCreate(chirpStackUrl, UriKind.Absolute, out var uri))
    {
        Console.WriteLine($"⚠️ ChirpStack URL inválida: {chirpStackUrl}");
    }
    else
    {
        Console.WriteLine($"🔗 ChirpStack URL (gRPC): {chirpStackUrl}");
    }
}

Console.WriteLine($"🔑 Token configurado: {(!string.IsNullOrEmpty(chirpStackToken) ? "Sim ✅" : "Não ⚠️")}");

builder.Services.AddSingleton<IChirpStackClient>(sp => 
{
    var logger = sp.GetService<ILogger<ChirpStackClient>>();
    try
    {
        return new ChirpStackClient(chirpStackUrl, chirpStackToken, logger);
    }
    catch (Exception ex)
    {
        logger?.LogError(ex, "Erro ao criar ChirpStackClient");
        throw;
    }
});

// Repositórios (EF Core)
builder.Services.AddScoped<ITenantRepository, TenantRepository>();
builder.Services.AddScoped<IGatewayRepository, GatewayRepository>();
builder.Services.AddScoped<ITenantUserRepository, TenantUserRepository>();
builder.Services.AddScoped<IApplicationRepository, ApplicationRepository>();
builder.Services.AddScoped<IDeviceRepository, DeviceRepository>();
builder.Services.AddScoped<IVpnNetworkRepository, VpnNetworkRepository>();
builder.Services.AddScoped<IRouterRepository, RouterRepository>();
builder.Services.AddScoped<IVpnPeerRepository, VpnPeerRepository>();
builder.Services.AddScoped<IAllowedNetworkRepository, AllowedNetworkRepository>();
builder.Services.AddScoped<IRemoteNetworkRepository, RemoteNetworkRepository>();
builder.Services.AddScoped<IStaticNetworkRepository, StaticNetworkRepository>();
builder.Services.AddScoped<IUserAllowedRouteRepository, Automais.Infrastructure.Repositories.UserAllowedRouteRepository>();
builder.Services.AddScoped<IRouterConfigLogRepository, RouterConfigLogRepository>();
builder.Services.AddScoped<IRouterBackupRepository, RouterBackupRepository>();
builder.Services.AddScoped<IHostRepository, HostRepository>();
builder.Services.AddScoped<IVpnIpAllocationService, VpnIpAllocationService>();
builder.Services.AddScoped<IHostService, HostService>();

// Services
builder.Services.AddScoped<ITenantService, TenantService>();
builder.Services.AddScoped<IGatewayService, GatewayService>();
builder.Services.AddScoped<IEmailService, Automais.Infrastructure.Services.EmailService>();
builder.Services.AddScoped<ITenantUserService, TenantUserService>();
builder.Services.AddScoped<IApplicationService, ApplicationService>();
builder.Services.AddScoped<IDeviceService, DeviceService>();
// Padrões do servidor VPN (endpoint default, etc.)
builder.Services.Configure<VpnDefaultsSettings>(
    builder.Configuration.GetSection("Vpn"));

// Configuração do serviço VPN Python
builder.Services.Configure<Automais.Infrastructure.Services.VpnServiceOptions>(
    builder.Configuration.GetSection("VpnService"));

// Configuração do serviço RouterOS Python
builder.Services.Configure<Automais.Infrastructure.Services.RouterOsServiceOptions>(
    builder.Configuration.GetSection("RouterOsService"));

// Configuração do serviço RouterOS WebSocket
builder.Services.Configure<Automais.Infrastructure.Services.RouterOsWebSocketOptions>(
    builder.Configuration.GetSection("RouterOsWebSocket"));

// Registrar HttpClient para serviço VPN Python
// IMPORTANTE: BaseAddress NÃO é configurado aqui porque as URLs são dinâmicas,
// baseadas no ServerEndpoint da VpnNetwork de cada router.
// Cada router pode estar associado a uma VpnNetwork diferente, que por sua vez
// pode ter um ServerEndpoint diferente, permitindo múltiplos servidores VPN.
builder.Services.AddHttpClient<Automais.Core.Interfaces.IVpnServiceClient, Automais.Infrastructure.Services.VpnServiceClient>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<Automais.Infrastructure.Services.VpnServiceOptions>>().Value;
    // NÃO configurar BaseAddress - URLs serão construídas dinamicamente por chamada
    // baseadas no ServerEndpoint da VpnNetwork
    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
})
.ConfigureHttpClient((sp, client) =>
{
    // Injetar dependências necessárias para buscar ServerEndpoint dinamicamente
    // Isso é feito via factory pattern no construtor do VpnServiceClient
});

// Registrar HttpClient para serviço RouterOS Python
// IMPORTANTE: BaseAddress NÃO é configurado aqui porque as URLs são dinâmicas,
// baseadas no ServerEndpoint da VpnNetwork de cada router.
// Cada router pode estar associado a uma VpnNetwork diferente, que por sua vez
// pode ter um ServerEndpoint diferente, permitindo múltiplos servidores RouterOS.
builder.Services.AddHttpClient<Automais.Core.Interfaces.IRouterOsServiceClient, Automais.Infrastructure.Services.RouterOsServiceClient>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<Automais.Infrastructure.Services.RouterOsServiceOptions>>().Value;
    // NÃO configurar BaseAddress - URLs serão construídas dinamicamente por chamada
    // baseadas no ServerEndpoint da VpnNetwork
    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
})
.ConfigureHttpClient((sp, client) =>
{
    // Injetar dependências necessárias para buscar ServerEndpoint dinamicamente
    // Isso é feito via factory pattern no construtor do RouterOsServiceClient
})
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    // Permitir certificados SSL auto-assinados em desenvolvimento
    ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
});

// Registrar cliente WebSocket RouterOS
builder.Services.AddScoped<Automais.Core.Interfaces.IRouterOsWebSocketClient, Automais.Infrastructure.Services.RouterOsWebSocketClient>();

builder.Services.AddScoped<IVpnPeerService>(sp =>
{
    var peerRepo = sp.GetRequiredService<IVpnPeerRepository>();
    var routerRepo = sp.GetRequiredService<IRouterRepository>();
    var vpnNetworkRepo = sp.GetRequiredService<IVpnNetworkRepository>();
    var vpnServiceClient = sp.GetRequiredService<Automais.Core.Interfaces.IVpnServiceClient>();
    var allowedRepo = sp.GetRequiredService<IAllowedNetworkRepository>();
    var remoteRepo = sp.GetRequiredService<IRemoteNetworkRepository>();
    var logger = sp.GetService<ILogger<Automais.Core.Services.VpnPeerService>>();
    return new Automais.Core.Services.VpnPeerService(peerRepo, routerRepo, vpnNetworkRepo, vpnServiceClient, allowedRepo, remoteRepo, logger);
});

builder.Services.AddScoped<IVpnNetworkService>(sp =>
{
    var tenantRepo = sp.GetRequiredService<ITenantRepository>();
    var vpnNetworkRepo = sp.GetRequiredService<IVpnNetworkRepository>();
    var deviceRepo = sp.GetRequiredService<IDeviceRepository>();
    var tenantUserService = sp.GetRequiredService<ITenantUserService>();
    var vpnDefaults = sp.GetRequiredService<IOptions<VpnDefaultsSettings>>();
    var vpnServiceClient = sp.GetService<Automais.Core.Interfaces.IVpnServiceClient>();
    var vpnPeerService = sp.GetRequiredService<IVpnPeerService>();
    return new VpnNetworkService(tenantRepo, vpnNetworkRepo, deviceRepo, tenantUserService, vpnDefaults, vpnServiceClient, vpnPeerService);
});

// Registrar RouterService com IVpnPeerService como dependência opcional
// RouterOsClient removido - comunicação RouterOS agora é feita via servidor VPN
builder.Services.AddScoped<IRouterService>(sp =>
{
    var routerRepo = sp.GetRequiredService<IRouterRepository>();
    var tenantRepo = sp.GetRequiredService<ITenantRepository>();
    var allowedNetworkRepo = sp.GetService<IAllowedNetworkRepository>();
    var vpnPeerService = sp.GetService<IVpnPeerService>(); // Opcional
    var vpnNetworkRepo = sp.GetService<IVpnNetworkRepository>(); // Opcional
    var logger = sp.GetService<ILogger<Automais.Core.Services.RouterService>>();
    var peerRepo = sp.GetService<IVpnPeerRepository>();
    return new Automais.Core.Services.RouterService(routerRepo, tenantRepo, allowedNetworkRepo, vpnPeerService, vpnNetworkRepo, peerRepo, logger);
});

builder.Services.AddScoped<IStaticNetworkService>(sp =>
{
    var routeRepo = sp.GetRequiredService<IStaticNetworkRepository>();
    var routerRepo = sp.GetRequiredService<IRouterRepository>();
    var logger = sp.GetService<ILogger<Automais.Core.Services.StaticNetworkService>>();
    return new Automais.Core.Services.StaticNetworkService(routeRepo, routerRepo, logger);
});

builder.Services.AddScoped<IAllowedNetworkService>(sp =>
{
    var allowedRepo = sp.GetRequiredService<IAllowedNetworkRepository>();
    var routerRepo = sp.GetRequiredService<IRouterRepository>();
    var logger = sp.GetService<ILogger<Automais.Core.Services.AllowedNetworkService>>();
    return new Automais.Core.Services.AllowedNetworkService(allowedRepo, routerRepo, logger);
});

builder.Services.AddScoped<IRemoteNetworkService>(sp =>
{
    var repo = sp.GetRequiredService<IRemoteNetworkRepository>();
    var routerRepo = sp.GetRequiredService<IRouterRepository>();
    var logger = sp.GetService<ILogger<RemoteNetworkService>>();
    return new RemoteNetworkService(repo, routerRepo, logger);
});

builder.Services.AddScoped<IAuthService, Automais.Infrastructure.Services.AuthService>();
builder.Services.AddScoped<IUserVpnService, Automais.Infrastructure.Services.UserVpnService>();

// SignalR para notificações em tempo real
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = true; // Habilitar erros detalhados para debug
})
.AddJsonProtocol(jsonOptions =>
{
    // Usar camelCase para compatibilidade com JavaScript/TypeScript
    jsonOptions.PayloadSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    jsonOptions.PayloadSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});

// Serviço de monitoramento de status dos roteadores foi removido
// O monitoramento está sendo feito pelo serviço Python (vpnserver.io)

// RouterBackupService com caminho de storage configurável
// RouterOsClient removido - comunicação RouterOS agora é feita via servidor VPN
var backupStoragePath = builder.Configuration["Backup:StoragePath"] ?? "/backups/routers";
builder.Services.AddScoped<IRouterBackupService>(sp =>
{
    var backupRepo = sp.GetRequiredService<IRouterBackupRepository>();
    var routerRepo = sp.GetRequiredService<IRouterRepository>();
    var tenantUserRepo = sp.GetService<ITenantUserRepository>();
    return new RouterBackupService(backupRepo, routerRepo, tenantUserRepo, backupStoragePath);
});

// External Clients
// IRouterOsClient removido - comunicação RouterOS agora é feita via servidor VPN (Python)

// Controllers com serialização JSON configurada
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Usar camelCase para compatibilidade com JavaScript/TypeScript
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        // Serializar enums como strings ao invés de números
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        // Ignorar propriedades nulas
        options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    });

// Configurar Kestrel com timeouts (evita requisições travadas)
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(30);
    
    // Configurar HTTPS apenas em produção usando certificado Let's Encrypt
    if (builder.Environment.IsProduction())
    {
        var certPath = "/etc/letsencrypt/live/automais.io";
        var certFile = Path.Combine(certPath, "fullchain.pem");
        var keyFile = Path.Combine(certPath, "privkey.pem");
        
        if (File.Exists(certFile) && File.Exists(keyFile))
        {
            try
            {
                // Ler certificado e chave privada em formato PEM
                var certContent = File.ReadAllText(certFile);
                var keyContent = File.ReadAllText(keyFile);
                
                // Converter PEM para X509Certificate2
                var certificate = X509Certificate2.CreateFromPem(certContent, keyContent);
                
                // Configurar HTTPS na porta 5001
                options.Listen(IPAddress.Any, 5001, listenOptions =>
                {
                    listenOptions.UseHttps(certificate);
                });
                
                Console.WriteLine("✅ HTTPS configurado na porta 5001 usando certificado Let's Encrypt");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Erro ao configurar HTTPS: {ex.Message}");
                Console.WriteLine("⚠️ Continuando apenas com HTTP (porta 5000)");
            }
        }
        else
        {
            Console.WriteLine($"⚠️ Certificados não encontrados em {certPath}");
            Console.WriteLine("⚠️ Continuando apenas com HTTP (porta 5000)");
        }
    }
    else
    {
        Console.WriteLine("🔧 Ambiente de desenvolvimento - HTTPS não configurado");
    }
});

// Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() 
    { 
        Title = "Automais IoT Platform API", 
        Version = "v1",
        Description = "API para gerenciamento de plataforma IoT multi-tenant (PostgreSQL). Inclui Auth (login, forgot-password), Tenants, Routers, etc."
    });
    // Garantir que todos os controllers (incluindo Auth) apareçam no Swagger
    c.DocInclusionPredicate((_, api) => true);
});

// CORS (para desenvolvimento e produção)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(
                "http://localhost:3000", 
                "http://localhost:5173",
                "https://automais.io",
                "https://www.automais.io",
                "https://api.automais.io",
                "https://automais.io:5001",
                "https://www.automais.io:5001"
              )
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials()
              .SetPreflightMaxAge(TimeSpan.FromHours(24)); // Cache preflight por 24h
    });
});

var app = builder.Build();

// Testar conexão com banco de dados na inicialização
var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("🔍 Iniciando teste de conexão com banco de dados...");

try
{
    using (var scope = app.Services.CreateScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        
        logger.LogInformation("📊 Tentando conectar ao banco de dados...");
        logger.LogInformation("📊 Host: {Host}", npgBuilder.Host);
        logger.LogInformation("📊 Port: {Port}", npgBuilder.Port);
        logger.LogInformation("📊 Database: {Database}", npgBuilder.Database);
        logger.LogInformation("📊 Username: {Username}", npgBuilder.Username);
        logger.LogInformation("📊 SSL Mode: {SslMode}", npgBuilder.SslMode);
        logger.LogInformation("📊 Command Timeout: {CommandTimeout}s", npgBuilder.CommandTimeout);
        logger.LogInformation("📊 Connection Timeout: {Timeout}s", npgBuilder.Timeout);
        
        // Tentar conectar e capturar erros detalhados
        try
        {
            logger.LogInformation("🔄 Tentando CanConnectAsync()...");
            var canConnect = await dbContext.Database.CanConnectAsync();
            logger.LogInformation("🔄 CanConnectAsync() retornou: {Result}", canConnect);
            
            if (canConnect)
            {
                logger.LogInformation("✅ Conexão com banco de dados estabelecida com sucesso!");
                
                // Testar uma query simples
                try
                {
                    logger.LogInformation("🔄 Executando query de teste (COUNT tenants)...");
                    var tenantCount = await dbContext.Set<Tenant>().CountAsync();
                    logger.LogInformation("✅ Query de teste executada com sucesso! Total de tenants: {Count}", tenantCount);
                }
                catch (Exception queryEx)
                {
                    logger.LogWarning(queryEx, "⚠️ Conexão OK, mas query de teste falhou: {Error}", queryEx.Message);
                    logger.LogWarning("⚠️ Stack Trace: {StackTrace}", queryEx.StackTrace);
                }
            }
            else
            {
                logger.LogError("❌ CanConnectAsync retornou false - não foi possível conectar ao banco de dados!");
                
                // Tentar uma conexão direta para ver o erro real
                logger.LogInformation("🔄 Tentando conexão direta com ExecuteSqlRawAsync('SELECT 1')...");
                try
                {
                    await dbContext.Database.ExecuteSqlRawAsync("SELECT 1");
                    logger.LogInformation("✅ ExecuteSqlRawAsync funcionou mesmo com CanConnectAsync=false");
                }
                catch (Exception directEx)
                {
                    logger.LogError(directEx, "❌ Erro ao executar query direta: {Error}", directEx.Message);
                    logger.LogError("❌ Tipo de exceção: {ExceptionType}", directEx.GetType().Name);
                    if (directEx.InnerException != null)
                    {
                        logger.LogError("❌ Inner Exception: {InnerException}", directEx.InnerException.Message);
                        logger.LogError("❌ Inner Exception Type: {InnerExceptionType}", directEx.InnerException.GetType().Name);
                        logger.LogError("❌ Inner Stack Trace: {InnerStackTrace}", directEx.InnerException.StackTrace);
                    }
                    logger.LogError("❌ Stack Trace completo: {StackTrace}", directEx.StackTrace);
                }
            }
        }
        catch (Npgsql.NpgsqlException npgEx)
        {
            logger.LogError(npgEx, "❌ Erro Npgsql ao testar conexão: {Error}", npgEx.Message);
            logger.LogError("❌ SQL State: {SqlState}", npgEx.SqlState);
            logger.LogError("❌ Code: {Code}", npgEx.ErrorCode);
            logger.LogError("❌ Inner Exception: {InnerException}", npgEx.InnerException?.Message);
            logger.LogError("❌ Stack Trace: {StackTrace}", npgEx.StackTrace);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "❌ Erro inesperado ao testar conexão: {Error}", ex.Message);
            logger.LogError("❌ Tipo de exceção: {ExceptionType}", ex.GetType().Name);
            logger.LogError("❌ Inner Exception: {InnerException}", ex.InnerException?.Message);
            logger.LogError("❌ Stack Trace: {StackTrace}", ex.StackTrace);
        }
    }
}
catch (Npgsql.NpgsqlException ex)
{
    logger.LogError(ex, "❌ Erro Npgsql ao conectar ao banco de dados: {Error}", ex.Message);
    logger.LogError("❌ Inner Exception: {InnerException}", ex.InnerException?.Message);
    logger.LogError("❌ SQL State: {SqlState}", ex.SqlState);
}
catch (Exception ex)
{
    logger.LogError(ex, "❌ Erro inesperado ao testar conexão com banco de dados: {Error}", ex.Message);
    logger.LogError("❌ Inner Exception: {InnerException}", ex.InnerException?.Message);
}

logger.LogInformation("🔍 Teste de conexão concluído.");

// ===== Configuração do Pipeline HTTP =====

// Middleware de logging de requisições (para debug)
app.Use(async (context, next) =>
{
    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
    var startTime = DateTime.UtcNow;
    
    try
    {
        await next();
        var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;
        
        if (context.Response.StatusCode >= 500)
        {
            logger.LogWarning("⚠️ Requisição {Method} {Path} retornou {StatusCode} em {Duration}ms", 
                context.Request.Method, context.Request.Path, context.Response.StatusCode, duration);
        }
        else if (duration > 5000) // Logar requisições lentas (>5s)
        {
            logger.LogWarning("🐌 Requisição lenta: {Method} {Path} levou {Duration}ms", 
                context.Request.Method, context.Request.Path, duration);
        }
    }
    catch (Exception ex)
    {
        var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;
        logger.LogError(ex, "❌ Erro não tratado na requisição {Method} {Path} após {Duration}ms: {Error}", 
            context.Request.Method, context.Request.Path, duration, ex.Message);
        throw;
    }
});

// Tratamento global de erros (deve vir primeiro)
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        var exception = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
        
        logger.LogError(exception, "❌ Erro não tratado: {Error} | Path: {Path} | Method: {Method}", 
            exception?.Message, context.Request.Path, context.Request.Method);
        
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";
        
        var response = new
        {
            message = "Erro interno do servidor",
            detail = app.Environment.IsDevelopment() ? exception?.ToString() : null,
            path = context.Request.Path,
            method = context.Request.Method,
            timestamp = DateTime.UtcNow
        };
        
        await context.Response.WriteAsJsonAsync(response);
    });
});

// Swagger sempre habilitado
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Automais IoT Platform API v1");
    c.RoutePrefix = "swagger"; // Swagger em /swagger
});

// Habilitar WebSocket antes do routing
app.UseWebSockets();

// Routing deve vir antes dos mapeamentos
app.UseRouting();

// CORS deve vir depois de UseRouting e antes de UseAuthorization
// IMPORTANTE: SignalR precisa de CORS configurado corretamente
// CORS também precisa estar antes de qualquer endpoint mapping
app.UseCors("AllowFrontend");

// Bloqueia demais rotas /api/* quando JWT exige troca de senha (senha temporária)
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? "";
    if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
    {
        await next();
        return;
    }

    if ((path.Equals("/api/auth/login", StringComparison.OrdinalIgnoreCase) ||
         path.Equals("/api/auth/forgot-password", StringComparison.OrdinalIgnoreCase)) &&
        context.Request.Method == HttpMethods.Post)
    {
        await next();
        return;
    }

    var auth = context.Request.Headers.Authorization.FirstOrDefault();
    if (string.IsNullOrEmpty(auth) || !auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
        await next();
        return;
    }

    var token = auth["Bearer ".Length..].Trim();
    var authService = context.RequestServices.GetRequiredService<Automais.Core.Interfaces.IAuthService>();
    var (valid, mustCh) = await authService.GetTokenPasswordChangeStateAsync(token, context.RequestAborted);
    if (valid && mustCh &&
        !(path.Equals("/api/auth/change-password", StringComparison.OrdinalIgnoreCase) &&
          context.Request.Method == HttpMethods.Post))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new
        {
            message = "Defina uma nova senha antes de continuar.",
            code = "MUST_CHANGE_PASSWORD"
        });
        return;
    }

    await next();
});

// Mapear endpoints - SignalR deve vir ANTES de MapControllers e UseAuthorization para evitar conflitos
// O endpoint de negociação do SignalR precisa ser acessível sem autenticação
app.MapHub<Automais.Core.Hubs.RouterStatusHub>("/hubs/router-status");

// Mapear WebSocket endpoint para proxy RouterOS (ANTES de UseAuthorization)
app.Map("/api/ws/routeros/{routerId:guid}", async (HttpContext context, Guid routerId) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("Expected WebSocket request");
        return;
    }

    var webSocket = await context.WebSockets.AcceptWebSocketAsync();
    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
    logger.LogInformation("WebSocket conectado para router {RouterId}", routerId);

    try
    {
        var routerRepository = context.RequestServices.GetRequiredService<Automais.Core.Interfaces.IRouterRepository>();
        var vpnNetworkRepository = context.RequestServices.GetRequiredService<Automais.Core.Interfaces.IVpnNetworkRepository>();
        var configuration = context.RequestServices.GetRequiredService<IConfiguration>();

        // Buscar router e obter ServerEndpoint
        var router = await routerRepository.GetByIdAsync(routerId, context.RequestAborted);
        if (router == null)
        {
            await SendWebSocketErrorAndClose(webSocket, "Router não encontrado", logger);
            return;
        }

        string? serverEndpoint = null;
        if (router.VpnNetworkId.HasValue)
        {
            var vpnNetwork = await vpnNetworkRepository.GetByIdAsync(router.VpnNetworkId.Value, context.RequestAborted);
            serverEndpoint = vpnNetwork?.ServerEndpoint;
        }

        // Construir URL do WebSocket do routeros.io
        // IMPORTANTE: Se o ServerEndpoint for o mesmo domínio da API, usar localhost
        // pois o routeros.io Python está rodando no mesmo servidor
        var requestHost = context.Request.Host.Host;
        var isHttps = context.Request.IsHttps || 
                     context.Request.Headers["X-Forwarded-Proto"].ToString().Equals("https", StringComparison.OrdinalIgnoreCase);
        
        string wsUrl;
        if (string.IsNullOrWhiteSpace(serverEndpoint))
        {
            // Se não tem ServerEndpoint, usar localhost (routeros.io está no mesmo servidor)
            wsUrl = "ws://localhost:8765";
            logger.LogInformation("ServerEndpoint não configurado, usando localhost:8765");
        }
        else
        {
            // Extrair apenas o hostname do ServerEndpoint (sem protocolo)
            string endpointHost = serverEndpoint;
            if (serverEndpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                endpointHost = serverEndpoint.Replace("http://", "").Split('/')[0].Split(':')[0];
            }
            else if (serverEndpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                endpointHost = serverEndpoint.Replace("https://", "").Split('/')[0].Split(':')[0];
            }
            else if (serverEndpoint.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) ||
                     serverEndpoint.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
            {
                endpointHost = serverEndpoint.Replace("ws://", "").Replace("wss://", "").Split('/')[0].Split(':')[0];
            }
            else
            {
                endpointHost = serverEndpoint.Split('/')[0].Split(':')[0];
            }

            // Se o ServerEndpoint for o mesmo domínio da requisição, usar localhost
            // (routeros.io Python está rodando no mesmo servidor)
            if (endpointHost.Equals(requestHost, StringComparison.OrdinalIgnoreCase) ||
                endpointHost.Equals("automais.io", StringComparison.OrdinalIgnoreCase) ||
                endpointHost.Equals("www.automais.io", StringComparison.OrdinalIgnoreCase))
            {
                wsUrl = "ws://localhost:8765";
                logger.LogInformation("ServerEndpoint {ServerEndpoint} é o mesmo domínio da API, usando localhost:8765", serverEndpoint);
            }
            else
            {
                // ServerEndpoint diferente - conectar diretamente
                var wsProtocol = isHttps ? "wss://" : "ws://";
                if (serverEndpoint.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) ||
                    serverEndpoint.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
                {
                    if (serverEndpoint.Contains(':', StringComparison.Ordinal) && 
                        !serverEndpoint.EndsWith("://", StringComparison.OrdinalIgnoreCase))
                    {
                        wsUrl = serverEndpoint;
                    }
                    else
                    {
                        wsUrl = $"{serverEndpoint}:8765";
                    }
                }
                else if (serverEndpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                {
                    var endpointWithoutProtocol = serverEndpoint.Replace("http://", "");
                    wsUrl = $"{wsProtocol}{endpointWithoutProtocol}:8765";
                }
                else if (serverEndpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    var endpointWithoutProtocol = serverEndpoint.Replace("https://", "");
                    wsUrl = $"wss://{endpointWithoutProtocol}:8765";
                }
                else
                {
                    wsUrl = $"{wsProtocol}{serverEndpoint}:8765";
                }
            }
        }

        logger.LogInformation("Conectando ao routeros.io em {WsUrl} para router {RouterId} (ServerEndpoint: {ServerEndpoint})", 
            wsUrl, routerId, serverEndpoint ?? "null");

        // Conectar ao servidor routeros.io Python
        var clientWebSocket = new System.Net.WebSockets.ClientWebSocket();
        try
        {
            logger.LogInformation("Tentando estabelecer conexão WebSocket com {WsUrl}...", wsUrl);
            await clientWebSocket.ConnectAsync(new Uri(wsUrl), context.RequestAborted);
            logger.LogInformation("✅ Conectado ao routeros.io com sucesso para router {RouterId}", routerId);

            // Fazer proxy bidirecional
            var cancellationToken = context.RequestAborted;
            var clientToServer = ProxyWebSocketMessages(webSocket, clientWebSocket, cancellationToken, logger);
            var serverToClient = ProxyWebSocketMessages(clientWebSocket, webSocket, cancellationToken, logger);

            // Aguardar até que uma das conexões feche
            await Task.WhenAny(clientToServer, serverToClient);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao conectar ao routeros.io em {WsUrl} para router {RouterId}", wsUrl, routerId);
            await SendWebSocketErrorAndClose(webSocket, $"Erro ao conectar ao servidor RouterOS: {ex.Message}", logger);
        }
        finally
        {
            // Fechar conexão com routeros.io
            if (clientWebSocket.State == WebSocketState.Open || clientWebSocket.State == WebSocketState.CloseReceived)
            {
                try
                {
                    await clientWebSocket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Connection closed",
                        CancellationToken.None);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Erro ao fechar conexão com routeros.io: {Error}", ex.Message);
                }
            }
            clientWebSocket.Dispose();
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Erro no WebSocket proxy para router {RouterId}", routerId);
    }
    finally
    {
        if (webSocket.State == System.Net.WebSockets.WebSocketState.Open || 
            webSocket.State == System.Net.WebSockets.WebSocketState.CloseReceived)
        {
            await webSocket.CloseAsync(
                System.Net.WebSockets.WebSocketCloseStatus.NormalClosure,
                "Connection closed",
                CancellationToken.None);
        }
        logger.LogInformation("WebSocket desconectado para router {RouterId}", routerId);
    }
});

// Proxy WebSocket para serviço Python hosts.io (SSH / console Linux) — porta 8766, loopback por padrão.
// Autenticação: JWT em query (?access_token= ou ?token=) antes do upgrade (browser não envia Authorization no WebSocket).
app.Map("/api/ws/hosts/{hostId:guid}", async (HttpContext context, Guid hostId) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("Expected WebSocket request");
        return;
    }

    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
    var authService = context.RequestServices.GetRequiredService<IAuthService>();
    var hostRepository = context.RequestServices.GetRequiredService<IHostRepository>();
    var configuration = context.RequestServices.GetRequiredService<IConfiguration>();

    var bearerToken = context.Request.Query["access_token"].FirstOrDefault()
                      ?? context.Request.Query["token"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(bearerToken))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { message = "Token ausente", code = "UNAUTHORIZED" });
        return;
    }

    var (tokenValid, mustChangePassword) =
        await authService.GetTokenPasswordChangeStateAsync(bearerToken, context.RequestAborted);
    if (!tokenValid)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { message = "Token inválido ou expirado", code = "UNAUTHORIZED" });
        return;
    }

    if (mustChangePassword)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new
        {
            message = "Defina uma nova senha antes de continuar.",
            code = "MUST_CHANGE_PASSWORD"
        });
        return;
    }

    var userInfo = await authService.ValidateTokenAsync(bearerToken, context.RequestAborted);
    if (userInfo == null)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { message = "Token inválido ou expirado", code = "UNAUTHORIZED" });
        return;
    }

    var host = await hostRepository.GetByIdAsync(hostId, context.RequestAborted);
    if (host == null)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        await context.Response.WriteAsJsonAsync(new { message = "Host não encontrado" });
        return;
    }

    if (host.TenantId != userInfo.TenantId)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new { message = "Acesso negado a este host", code = "FORBIDDEN" });
        return;
    }

    const int hostsPythonPort = 8766;
    string wsUrl;

    var configuredBackend = configuration["HostsWebSocket:BackendUrl"]?.Trim();
    if (!string.IsNullOrWhiteSpace(configuredBackend))
    {
        wsUrl = configuredBackend.TrimEnd('/');
        if (!wsUrl.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) &&
            !wsUrl.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
        {
            wsUrl = $"ws://{wsUrl}";
        }

        logger.LogInformation("Hosts WebSocket: HostsWebSocket:BackendUrl → {WsUrl}", wsUrl);
    }
    else
    {
        wsUrl = $"ws://127.0.0.1:{hostsPythonPort}";
        logger.LogInformation("Hosts WebSocket: backend padrão loopback {WsUrl}", wsUrl);
    }

    // Conectar ao Python ANTES do upgrade no browser: evita onopen seguido de close imediato
    // (race em que o front acha o WSS aberto mas o proxy já derrubou por ECONNREFUSED na 8766).
    logger.LogInformation("Conectando ao hosts.io em {WsUrl} para host {HostId} (antes do AcceptWebSocket)", wsUrl, hostId);

    var upstreamWebSocket = new System.Net.WebSockets.ClientWebSocket();
    try
    {
        await upstreamWebSocket.ConnectAsync(new Uri(wsUrl), context.RequestAborted);
        logger.LogInformation("Upstream hosts.io OK para host {HostId}", hostId);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Erro ao conectar ao hosts.io em {WsUrl} para host {HostId}", wsUrl, hostId);
        upstreamWebSocket.Dispose();
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsJsonAsync(new
        {
            message =
                "Serviço Hosts (Python) indisponível. Inicie Automais.IO.hosts na porta 8766 no mesmo servidor da API ou ajuste HostsWebSocket:BackendUrl.",
            code = "HOSTS_UPSTREAM_UNAVAILABLE",
            detail = ex.Message
        });
        return;
    }

    WebSocket browserWebSocket;
    try
    {
        browserWebSocket = await context.WebSockets.AcceptWebSocketAsync();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Falha ao aceitar WebSocket do browser para host {HostId}", hostId);
        await CloseHostsUpstreamQuietlyAsync(upstreamWebSocket, logger);
        upstreamWebSocket.Dispose();
        return;
    }

    logger.LogInformation("WebSocket browser conectado para host {HostId} (tenant {TenantId})", hostId, userInfo.TenantId);

    try
    {
        var cancellationToken = context.RequestAborted;
        // Injeta terminalUserId / terminalTenantId (JWT) em cada frame terminal_* — o Python confia nisto, não no browser.
        var browserToUpstream = ProxyHostsBrowserToUpstream(
            browserWebSocket,
            upstreamWebSocket,
            userInfo.Id,
            userInfo.TenantId,
            cancellationToken,
            logger);
        var upstreamToBrowser = ProxyWebSocketMessages(upstreamWebSocket, browserWebSocket, cancellationToken, logger);
        await Task.WhenAny(browserToUpstream, upstreamToBrowser);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Erro no WebSocket proxy para host {HostId}", hostId);
    }
    finally
    {
        await CloseHostsUpstreamQuietlyAsync(upstreamWebSocket, logger);
        upstreamWebSocket.Dispose();

        if (browserWebSocket.State == WebSocketState.Open ||
            browserWebSocket.State == WebSocketState.CloseReceived)
        {
            try
            {
                await browserWebSocket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Connection closed",
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Erro ao fechar WebSocket do browser (hosts): {Error}", ex.Message);
            }
        }

        logger.LogInformation("WebSocket desconectado para host {HostId}", hostId);
    }
});

// Proxy WebSocket para serviço Python remote.io (VNC / display remoto) — porta 8767, loopback por padrão.
// Autenticação: JWT em query (?access_token= ou ?token=). Tráfego binário RFB (sem injeção JSON).
app.Map("/api/ws/remote/{hostId:guid}", async (HttpContext context, Guid hostId) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("Expected WebSocket request");
        return;
    }

    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
    var authService = context.RequestServices.GetRequiredService<IAuthService>();
    var hostRepository = context.RequestServices.GetRequiredService<IHostRepository>();
    var configuration = context.RequestServices.GetRequiredService<IConfiguration>();

    var bearerToken = context.Request.Query["access_token"].FirstOrDefault()
                      ?? context.Request.Query["token"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(bearerToken))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { message = "Token ausente", code = "UNAUTHORIZED" });
        return;
    }

    var (tokenValid, mustChangePassword) =
        await authService.GetTokenPasswordChangeStateAsync(bearerToken, context.RequestAborted);
    if (!tokenValid)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { message = "Token inválido ou expirado", code = "UNAUTHORIZED" });
        return;
    }

    if (mustChangePassword)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new
        {
            message = "Defina uma nova senha antes de continuar.",
            code = "MUST_CHANGE_PASSWORD"
        });
        return;
    }

    var userInfo = await authService.ValidateTokenAsync(bearerToken, context.RequestAborted);
    if (userInfo == null)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { message = "Token inválido ou expirado", code = "UNAUTHORIZED" });
        return;
    }

    var host = await hostRepository.GetByIdAsync(hostId, context.RequestAborted);
    if (host == null)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        await context.Response.WriteAsJsonAsync(new { message = "Host não encontrado" });
        return;
    }

    if (host.TenantId != userInfo.TenantId)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new { message = "Acesso negado a este host", code = "FORBIDDEN" });
        return;
    }

    if (!host.RemoteDisplayEnabled)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new
        {
            message = "Display remoto desabilitado para este host.",
            code = "REMOTE_DISPLAY_DISABLED"
        });
        return;
    }

    const int remotePythonPort = 8767;
    string wsBase;

    var configuredRemoteBackend = configuration["RemoteWebSocket:BackendUrl"]?.Trim();
    if (!string.IsNullOrWhiteSpace(configuredRemoteBackend))
    {
        wsBase = configuredRemoteBackend.TrimEnd('/');
        if (!wsBase.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) &&
            !wsBase.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
        {
            wsBase = $"ws://{wsBase}";
        }

        logger.LogInformation("Remote WebSocket: RemoteWebSocket:BackendUrl → {WsBase}", wsBase);
    }
    else
    {
        wsBase = $"ws://127.0.0.1:{remotePythonPort}";
        logger.LogInformation("Remote WebSocket: backend padrão loopback {WsBase}", wsBase);
    }

    var upstreamUri = new Uri($"{wsBase.TrimEnd('/')}/{hostId:D}");
    // Aceitar o WebSocket do browser antes do upstream: se respondermos 503 JSON, o browser
    // falha o handshake com código 1006 (sem Upgrade). Com o handshake OK, podemos fechar com 1011.
    WebSocket browserWebSocket;
    try
    {
        browserWebSocket = await context.WebSockets.AcceptWebSocketAsync();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Falha ao aceitar WebSocket do browser para remote host {HostId}", hostId);
        return;
    }

    logger.LogInformation("Conectando ao remote.io em {UpstreamUri} após AcceptWebSocket (host {HostId})", upstreamUri, hostId);

    var upstreamWebSocket = new System.Net.WebSockets.ClientWebSocket();
    try
    {
        await upstreamWebSocket.ConnectAsync(upstreamUri, context.RequestAborted);
        logger.LogInformation("Upstream remote.io OK para host {HostId}", hostId);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Erro ao conectar ao remote.io em {UpstreamUri} para host {HostId}", upstreamUri, hostId);
        upstreamWebSocket.Dispose();
        try
        {
            if (browserWebSocket.State == WebSocketState.Open ||
                browserWebSocket.State == WebSocketState.CloseReceived)
            {
                await browserWebSocket.CloseAsync(
                    WebSocketCloseStatus.InternalServerError,
                    "Remote Python indisponivel (porta 8767 ou RemoteWebSocket:BackendUrl).",
                    CancellationToken.None);
            }
        }
        catch (Exception closeEx)
        {
            logger.LogWarning(closeEx, "Ao fechar WebSocket do browser após falha do upstream remote");
        }

        return;
    }

    logger.LogInformation("WebSocket browser (remote display) conectado para host {HostId} (tenant {TenantId})", hostId, userInfo.TenantId);

    try
    {
        var cancellationToken = context.RequestAborted;
        var browserToUpstream = ProxyWebSocketMessages(browserWebSocket, upstreamWebSocket, cancellationToken, logger);
        var upstreamToBrowser = ProxyWebSocketMessages(upstreamWebSocket, browserWebSocket, cancellationToken, logger);
        await Task.WhenAny(browserToUpstream, upstreamToBrowser);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Erro no WebSocket proxy (remote) para host {HostId}", hostId);
    }
    finally
    {
        await CloseHostsUpstreamQuietlyAsync(upstreamWebSocket, logger);
        upstreamWebSocket.Dispose();

        if (browserWebSocket.State == WebSocketState.Open ||
            browserWebSocket.State == WebSocketState.CloseReceived)
        {
            try
            {
                await browserWebSocket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Connection closed",
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Erro ao fechar WebSocket do browser (remote): {Error}", ex.Message);
            }
        }

        logger.LogInformation("WebSocket (remote) desconectado para host {HostId}", hostId);
    }
});

// WebSocket agente WebDevice (ESP → API → Python). Path = DevEUI (hex). Query ?token= (token WebDevice em claro).
app.Map("/api/ws/webdevice/agent/{devEui}", async (HttpContext context, string devEui) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("Expected WebSocket request");
        return;
    }

    if (!DevEuiNormalizer.TryNormalize(devEui, out var normDevEui))
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new { message = "DevEUI inválido.", code = "WEBDEVICE_DEVEUI_INVALID" });
        return;
    }

    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
    var configuration = context.RequestServices.GetRequiredService<IConfiguration>();
    var token = context.Request.Query["token"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(token))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { message = "Token ausente", code = "WEBDEVICE_TOKEN_MISSING" });
        return;
    }

    var backend = configuration["Webdevice:BackendUrl"]?.Trim();
    if (string.IsNullOrWhiteSpace(backend))
        backend = "http://127.0.0.1:8768";
    backend = backend.TrimEnd('/');
    if (!backend.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
        !backend.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        backend = "http://" + backend;

    string wsBase;
    if (backend.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        wsBase = "wss://" + backend["https://".Length..];
    else if (backend.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        wsBase = "ws://" + backend["http://".Length..];
    else
        wsBase = "ws://" + backend;

    var upstreamUri = new Uri(
        $"{wsBase.TrimEnd('/')}/agent/{normDevEui}?token={Uri.EscapeDataString(token.Trim())}");

    WebSocket? browserSide = null;
    using var upstreamWebSocket = new ClientWebSocket();
    try
    {
        await upstreamWebSocket.ConnectAsync(upstreamUri, context.RequestAborted);
        logger.LogInformation("Upstream webdevice agent OK DevEUI {DevEui}", normDevEui);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Falha ao conectar webdevice em {Uri} DevEUI {DevEui}", upstreamUri, normDevEui);
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsJsonAsync(new
        {
            message = "Serviço WebDevice indisponível.",
            code = "WEBDEVICE_UPSTREAM_UNAVAILABLE",
            detail = ex.Message
        });
        return;
    }

    try
    {
        browserSide = await context.WebSockets.AcceptWebSocketAsync();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Falha ao aceitar WebSocket do agente WebDevice {DevEui}", normDevEui);
        return;
    }

    try
    {
        var ct = context.RequestAborted;
        var t1 = ProxyWebSocketMessages(browserSide, upstreamWebSocket, ct, logger);
        var t2 = ProxyWebSocketMessages(upstreamWebSocket, browserSide, ct, logger);
        await Task.WhenAny(t1, t2);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Erro no proxy WebSocket WebDevice agent {DevEui}", normDevEui);
    }
    finally
    {
        if (upstreamWebSocket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            try
            {
                await upstreamWebSocket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "closed",
                    CancellationToken.None);
            }
            catch { /* ignore */ }
        }

        if (browserSide != null &&
            (browserSide.State == WebSocketState.Open || browserSide.State == WebSocketState.CloseReceived))
        {
            try
            {
                await browserSide.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Connection closed",
                    CancellationToken.None);
            }
            catch { /* ignore */ }
        }
    }
});

// Proxy HTTP UI do device (browser autenticado JWT) → Python webdevice. Path = DevEUI do device.
app.MapMethods(
    "/api/devices/{devEui}/web-ui/{**path}",
    new[] { "GET", "HEAD", "POST", "PUT", "DELETE", "OPTIONS" },
    async (HttpContext context, string devEui) =>
    {
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        var authService = context.RequestServices.GetRequiredService<IAuthService>();
        var deviceRepository = context.RequestServices.GetRequiredService<IDeviceRepository>();
        var configuration = context.RequestServices.GetRequiredService<IConfiguration>();

        if (HttpMethods.IsOptions(context.Request.Method))
        {
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            context.Response.Headers.Append("Allow", "GET, HEAD, POST, PUT, DELETE, OPTIONS");
            return;
        }

        // Esta rota precisa ser embutível no frontend (iframe) para WebDevice.
        // Mantemos o escopo restrito aos domínios oficiais do painel.
        context.Response.Headers.Remove("X-Frame-Options");
        context.Response.Headers["Content-Security-Policy"] =
            "frame-ancestors 'self' https://automais.io https://www.automais.io";

        var bearerToken = context.Request.Query["access_token"].FirstOrDefault()
                          ?? context.Request.Query["token"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(bearerToken))
        {
            var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
            if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                bearerToken = authHeader["Bearer ".Length..].Trim();
        }

        if (string.IsNullOrWhiteSpace(bearerToken))
        {
            logger.LogWarning("WebUI proxy: token ausente DevEUI {DevEui} Path {Path}", devEui, context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { message = "Token ausente", code = "UNAUTHORIZED" });
            return;
        }

        var (tokenValid, mustChangePassword) =
            await authService.GetTokenPasswordChangeStateAsync(bearerToken, context.RequestAborted);
        if (!tokenValid)
        {
            logger.LogWarning("WebUI proxy: token inválido/expirado DevEUI {DevEui}", devEui);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { message = "Token inválido ou expirado", code = "UNAUTHORIZED" });
            return;
        }

        if (mustChangePassword)
        {
            logger.LogWarning("WebUI proxy: MUST_CHANGE_PASSWORD DevEUI {DevEui}", devEui);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                message = "Defina uma nova senha antes de continuar.",
                code = "MUST_CHANGE_PASSWORD"
            });
            return;
        }

        var userInfo = await authService.ValidateTokenAsync(bearerToken, context.RequestAborted);
        if (userInfo == null)
        {
            logger.LogWarning("WebUI proxy: ValidateToken retornou nulo DevEUI {DevEui}", devEui);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { message = "Token inválido", code = "UNAUTHORIZED" });
            return;
        }

        if (!DevEuiNormalizer.TryNormalize(devEui, out var normUi))
        {
            logger.LogWarning("WebUI proxy: DevEUI inválido recebido {DevEui}", devEui);
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { message = "DevEUI inválido." });
            return;
        }

        var device = await deviceRepository.GetByDevEuiAsync(userInfo.TenantId, normUi, context.RequestAborted);
        if (device == null)
        {
            logger.LogWarning("WebUI proxy: device não encontrado tenant {TenantId} DevEUI {DevEui}",
                userInfo.TenantId, normUi);
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsJsonAsync(new { message = "Device não encontrado." });
            return;
        }

        if (!device.WebDeviceEnabled)
        {
            logger.LogWarning("WebUI proxy: WebDevice desabilitado tenant {TenantId} DevEUI {DevEui}",
                userInfo.TenantId, device.DevEui);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { message = "WebDevice não habilitado para este device." });
            return;
        }

        var subPath = context.Request.Path.Value ?? "";
        var prefix = $"/api/devices/{device.DevEui}/web-ui";
        var rel = "/";
        if (subPath.Length > prefix.Length &&
            subPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            rel = subPath[prefix.Length..];
            if (string.IsNullOrEmpty(rel))
                rel = "/";
            else if (rel[0] != '/')
                rel = "/" + rel;
        }

        var query = context.Request.QueryString.HasValue ? context.Request.QueryString.Value!.TrimStart('?') : "";

        var forwardHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[]
                 {
                     "Accept", "Accept-Language", "Authorization", "Content-Type", "Cookie",
                     "If-None-Match", "If-Modified-Since", "Origin", "Referer", "User-Agent"
                 })
        {
            var v = context.Request.Headers[name].FirstOrDefault();
            if (!string.IsNullOrEmpty(v))
                forwardHeaders[name] = v!;
        }

        string? bodyB64 = null;
        if (context.Request.ContentLength is > 0 or null &&
            (HttpMethods.IsPost(context.Request.Method) ||
             HttpMethods.IsPut(context.Request.Method) ||
             HttpMethods.IsDelete(context.Request.Method)))
        {
            await using var ms = new MemoryStream();
            await context.Request.Body.CopyToAsync(ms, context.RequestAborted);
            var buf = ms.ToArray();
            if (buf.Length > 0)
                bodyB64 = Convert.ToBase64String(buf);
        }

        var backendHttp = configuration["Webdevice:BackendUrl"]?.Trim();
        if (string.IsNullOrWhiteSpace(backendHttp))
            backendHttp = "http://127.0.0.1:8768";
        backendHttp = backendHttp.TrimEnd('/');

        var internalKey = configuration["InternalApiKey"]
                          ?? configuration["Automais:InternalApiKey"]
                          ?? Environment.GetEnvironmentVariable("AUTOMAIS_INTERNAL_API_KEY");

        var proxyDict = new Dictionary<string, object?>
        {
            ["method"] = context.Request.Method,
            ["path"] = rel,
            ["query"] = query,
            ["headers"] = forwardHeaders,
            ["body_base64"] = bodyB64
        };
        var proxyJson = JsonSerializer.Serialize(proxyDict);

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        using var req = new HttpRequestMessage(
            HttpMethod.Post,
            $"{backendHttp}/internal/proxy/{device.DevEui}");
        if (!string.IsNullOrWhiteSpace(internalKey))
            req.Headers.TryAddWithoutValidation("X-Automais-Internal-Key", internalKey.Trim());
        req.Content = new StringContent(proxyJson, Encoding.UTF8, "application/json");

        HttpResponseMessage pyRes;
        try
        {
            pyRes = await httpClient.SendAsync(req, context.RequestAborted);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "WebDevice proxy HTTP falhou DevEUI {DevEui}", device.DevEui);
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new
            {
                message = "Serviço WebDevice indisponível.",
                code = "WEBDEVICE_PROXY_UNAVAILABLE"
            });
            return;
        }

        var json = await pyRes.Content.ReadAsStringAsync(context.RequestAborted);
        if (!pyRes.IsSuccessStatusCode)
        {
            context.Response.StatusCode = (int)pyRes.StatusCode;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsync(json, context.RequestAborted);
            return;
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var status = root.TryGetProperty("status", out var st) ? st.GetInt32() : 502;
        byte[] bodyBytes = Array.Empty<byte>();
        if (root.TryGetProperty("body_base64", out var b64El) && b64El.ValueKind == JsonValueKind.String)
        {
            var s = b64El.GetString();
            if (!string.IsNullOrEmpty(s))
                bodyBytes = Convert.FromBase64String(s);
        }

        context.Response.StatusCode = status;

        string? respContentType = null;
        if (root.TryGetProperty("headers", out var hdrs) && hdrs.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in hdrs.EnumerateObject())
            {
                if (p.Value.ValueKind != JsonValueKind.String)
                    continue;
                var key = p.Name;
                if (key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                {
                    respContentType = p.Value.GetString();
                    continue;
                }

                context.Response.Headers[key] = p.Value.GetString();
            }
        }

        if (!string.IsNullOrEmpty(respContentType))
            context.Response.ContentType = respContentType;

        var contentType = respContentType ?? "application/octet-stream";
        if (contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase) &&
            bodyBytes.Length > 0)
        {
            var html = Encoding.UTF8.GetString(bodyBytes);
            var baseHref = $"{prefix}/";
            if (!html.Contains("<base ", StringComparison.OrdinalIgnoreCase))
            {
                var injected = $"<base href=\"{baseHref}\">";
                var idx = html.IndexOf("<head", StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    var gt = html.IndexOf('>', idx);
                    if (gt > idx)
                    {
                        html = html.Insert(gt + 1, injected);
                        bodyBytes = Encoding.UTF8.GetBytes(html);
                    }
                }
            }
        }

        context.Response.ContentLength = bodyBytes.Length;
        await context.Response.Body.WriteAsync(bodyBytes, context.RequestAborted);
    });

// Authorization (opcional para SignalR, mas necessário para APIs)
app.UseAuthorization();

// Mapear controllers
app.MapControllers();

// Endpoint de tratamento de erros (mantido para compatibilidade, mas o middleware acima já trata)

// Health check robusto
app.MapGet("/health", async (ApplicationDbContext dbContext, ILogger<Program> healthLogger) =>
{
    var healthStatus = new
    {
        status = "healthy",
        mode = "database",
        database = "postgresql (DigitalOcean)",
        chirpstack = chirpStackUrl,
        timestamp = DateTime.UtcNow,
        checks = new Dictionary<string, object>()
    };
    
    // Testar conexão com banco de dados
    try
    {
        var canConnect = await dbContext.Database.CanConnectAsync();
        healthStatus.checks["database"] = new
        {
            status = canConnect ? "healthy" : "unhealthy",
            connected = canConnect
        };
        
        if (!canConnect)
        {
            healthLogger.LogWarning("⚠️ Health check: Banco de dados não está acessível");
            return Results.Json(new
            {
                status = "unhealthy",
                checks = healthStatus.checks,
                timestamp = DateTime.UtcNow
            }, statusCode: 503);
        }
        
        // Testar query simples
        var testQuery = await dbContext.Database.ExecuteSqlRawAsync("SELECT 1");
        healthStatus.checks["database_query"] = new
        {
            status = "healthy",
            query_executed = true
        };
    }
    catch (Exception ex)
    {
        healthLogger.LogError(ex, "❌ Health check falhou: {Error}", ex.Message);
        healthStatus.checks["database"] = new
        {
            status = "unhealthy",
            error = ex.Message
        };
        return Results.Json(new
        {
            status = "unhealthy",
            checks = healthStatus.checks,
            timestamp = DateTime.UtcNow
        }, statusCode: 503);
    }
    
    return Results.Ok(healthStatus);
});

Console.WriteLine("\n🚀 API rodando!");
if (app.Environment.IsProduction())
{
    Console.WriteLine($"🔒 HTTPS: https://automais.io:5001");
    Console.WriteLine($"📝 Swagger: https://automais.io:5001/swagger");
    Console.WriteLine($"❤️  Health: https://automais.io:5001/health");
}
else
{
    Console.WriteLine($"📝 Swagger: http://localhost:5000/swagger ou https://localhost:5001/swagger");
    Console.WriteLine($"❤️  Health: http://localhost:5000/health");
}
Console.WriteLine($"💾 Modo: Postgres (DigitalOcean)");
Console.WriteLine($"📡 ChirpStack: {chirpStackUrl}\n");

app.Run();

// ===== Helper Functions =====

/// <summary>
/// Mascara informações sensíveis da connection string para logs
/// </summary>
static string MaskConnectionString(string connectionString)
{
    if (string.IsNullOrWhiteSpace(connectionString))
        return "(vazia)";

    // Mascara senha e outros dados sensíveis
    var masked = connectionString;
    var patterns = new[] { "Password=", "Pwd=", "User ID=", "Username=", "User=" };
    
    foreach (var pattern in patterns)
    {
        var index = masked.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
        if (index >= 0)
        {
            var start = index + pattern.Length;
            var end = masked.IndexOf(';', start);
            if (end < 0) end = masked.Length;
            
            var length = end - start;
            masked = masked.Substring(0, start) + new string('*', Math.Min(length, 10)) + masked.Substring(end);
        }
    }
    
    return masked;
}

/// <summary>
/// Substitui variáveis de ambiente no formato ${VAR} pelos valores reais
/// </summary>
static string ReplaceEnvironmentVariables(string input)
{
    if (string.IsNullOrWhiteSpace(input))
        return input;

    var result = input;
    var startIndex = 0;
    var missingVars = new List<string>();

    while ((startIndex = result.IndexOf("${", startIndex)) != -1)
    {
        var endIndex = result.IndexOf("}", startIndex);
        if (endIndex == -1)
        {
            Console.WriteLine($"⚠️ Variável de ambiente malformada: {result.Substring(startIndex)}");
            break;
        }

        var varName = result.Substring(startIndex + 2, endIndex - startIndex - 2);
        var envValue = Environment.GetEnvironmentVariable(varName);
        
        if (string.IsNullOrEmpty(envValue))
        {
            Console.WriteLine($"❌ Variável de ambiente '{varName}' não encontrada!");
            missingVars.Add(varName);
            envValue = string.Empty; // Substitui por string vazia para não quebrar o formato
        }
        else
        {
            Console.WriteLine($"✅ Variável '{varName}' encontrada (valor mascarado)");
        }
        
        result = result.Substring(0, startIndex) + envValue + result.Substring(endIndex + 1);
        startIndex += envValue.Length;
    }

    if (missingVars.Any())
    {
        throw new InvalidOperationException(
            $"Variáveis de ambiente não encontradas: {string.Join(", ", missingVars)}. " +
            "Verifique se as variáveis estão configuradas no systemd service ou no ambiente.");
    }

    return result;
}

/// <summary>
/// Browser → Python hosts: acumula texto UTF-8 e injeta identidade do painel (JWT) em mensagens <c>terminal_*</c>.
/// </summary>
static async Task ProxyHostsBrowserToUpstream(
    WebSocket browser,
    ClientWebSocket upstream,
    Guid terminalUserId,
    Guid terminalTenantId,
    CancellationToken cancellationToken,
    ILogger logger)
{
    var buffer = new byte[65536];
    using var accum = new MemoryStream();
    try
    {
        while (browser.State == WebSocketState.Open && upstream.State == WebSocketState.Open)
        {
            var result = await browser.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                if (upstream.State == WebSocketState.Open)
                {
                    await upstream.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Connection closed by browser",
                        cancellationToken);
                }
                break;
            }

            if (result.MessageType == WebSocketMessageType.Binary)
            {
                await upstream.SendAsync(
                    new ArraySegment<byte>(buffer, 0, result.Count),
                    WebSocketMessageType.Binary,
                    result.EndOfMessage,
                    cancellationToken);
                continue;
            }

            if (result.MessageType == WebSocketMessageType.Text)
            {
                accum.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage)
                    continue;

                var payload = accum.ToArray();
                accum.SetLength(0);
                var forwarded = TryInjectTrustedTerminalIdentity(
                    payload.AsSpan(),
                    terminalUserId,
                    terminalTenantId);
                if (forwarded != null)
                    payload = forwarded;
                await upstream.SendAsync(
                    new ArraySegment<byte>(payload),
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    cancellationToken);
            }
        }
    }
    catch (WebSocketException ex)
    {
        logger.LogWarning(ex, "Hosts WS browser→upstream: {Error}", ex.Message);
    }
    catch (OperationCanceledException)
    {
        logger.LogInformation("Hosts WS browser→upstream cancelado");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Hosts WS browser→upstream: {Error}", ex.Message);
    }
}

/// <summary>
/// Sobrescreve terminalUserId / terminalTenantId no JSON (ação <c>terminal_*</c>). Retorna null se não for caso de injeção.
/// </summary>
static byte[]? TryInjectTrustedTerminalIdentity(
    ReadOnlySpan<byte> utf8Payload,
    Guid terminalUserId,
    Guid terminalTenantId)
{
    try
    {
        using var doc = JsonDocument.Parse(utf8Payload.ToArray());
        var root = doc.RootElement;
        if (!root.TryGetProperty("action", out var actEl))
            return null;
        var action = actEl.GetString();
        if (string.IsNullOrEmpty(action) || !action.StartsWith("terminal_", StringComparison.Ordinal))
            return null;

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var prop in root.EnumerateObject())
            {
                if (prop.NameEquals("terminalUserId") || prop.NameEquals("terminalTenantId"))
                    continue;
                prop.WriteTo(writer);
            }
            writer.WriteString("terminalUserId", terminalUserId.ToString("D"));
            writer.WriteString("terminalTenantId", terminalTenantId.ToString("D"));
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }
    catch (JsonException)
    {
        return null;
    }
}

/// <summary>
/// Faz proxy de mensagens entre dois WebSockets
/// </summary>
static async Task ProxyWebSocketMessages(WebSocket source, WebSocket destination, CancellationToken cancellationToken, ILogger logger)
{
    var buffer = new byte[65536];
    try
    {
        while (source.State == WebSocketState.Open && destination.State == WebSocketState.Open)
        {
            var result = await source.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            
            if (result.MessageType == WebSocketMessageType.Close)
            {
                if (destination.State == WebSocketState.Open)
                {
                    await destination.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Connection closed by source",
                        cancellationToken);
                }
                break;
            }

            if (result.MessageType == WebSocketMessageType.Text || result.MessageType == WebSocketMessageType.Binary)
            {
                await destination.SendAsync(
                    new ArraySegment<byte>(buffer, 0, result.Count),
                    result.MessageType,
                    result.EndOfMessage,
                    cancellationToken);
            }
        }
    }
    catch (WebSocketException ex)
    {
        logger.LogWarning(ex, "WebSocket error durante proxy: {Error}", ex.Message);
    }
    catch (OperationCanceledException)
    {
        logger.LogInformation("Proxy cancelado");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Erro inesperado durante proxy: {Error}", ex.Message);
    }
}

/// <summary>
/// Envia mensagem de erro e fecha conexão WebSocket
/// </summary>
static async Task SendWebSocketErrorAndClose(WebSocket webSocket, string errorMessage, ILogger logger)
{
    try
    {
        var errorJson = JsonSerializer.Serialize(new { error = errorMessage });
        var errorBytes = Encoding.UTF8.GetBytes(errorJson);
        await webSocket.SendAsync(
            new ArraySegment<byte>(errorBytes),
            WebSocketMessageType.Text,
            true,
            CancellationToken.None);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Erro ao enviar mensagem de erro: {Error}", ex.Message);
    }
    finally
    {
        if (webSocket.State == WebSocketState.Open)
        {
            await webSocket.CloseAsync(
                WebSocketCloseStatus.InternalServerError,
                errorMessage,
                CancellationToken.None);
        }
    }
}

static async Task CloseHostsUpstreamQuietlyAsync(ClientWebSocket webSocket, ILogger logger)
{
    try
    {
        if (webSocket.State == WebSocketState.Open || webSocket.State == WebSocketState.CloseReceived)
        {
            await webSocket.CloseAsync(
                WebSocketCloseStatus.NormalClosure,
                "Connection closed",
                CancellationToken.None);
        }
    }
    catch (Exception ex)
    {
        logger.LogDebug(ex, "Ao fechar upstream Hosts (Python): {Error}", ex.Message);
    }
}

