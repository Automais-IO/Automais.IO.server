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
builder.Services.AddScoped<IRouterWireGuardPeerRepository, RouterWireGuardPeerRepository>();
builder.Services.AddScoped<IRouterAllowedNetworkRepository, RouterAllowedNetworkRepository>();
builder.Services.AddScoped<IRouterStaticRouteRepository, Automais.Infrastructure.Repositories.RouterStaticRouteRepository>();
builder.Services.AddScoped<IUserAllowedRouteRepository, Automais.Infrastructure.Repositories.UserAllowedRouteRepository>();
builder.Services.AddScoped<IRouterConfigLogRepository, RouterConfigLogRepository>();
builder.Services.AddScoped<IRouterBackupRepository, RouterBackupRepository>();

// Services
builder.Services.AddScoped<ITenantService, TenantService>();
builder.Services.AddScoped<IGatewayService, GatewayService>();
builder.Services.AddScoped<IEmailService, Automais.Infrastructure.Services.EmailService>();
builder.Services.AddScoped<ITenantUserService, TenantUserService>();
builder.Services.AddScoped<IApplicationService, ApplicationService>();
builder.Services.AddScoped<IDeviceService, DeviceService>();
// Configuração do WireGuard (mantido para compatibilidade, mas não usado mais)
builder.Services.Configure<WireGuardSettings>(
    builder.Configuration.GetSection("WireGuard"));

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

builder.Services.AddScoped<IRouterWireGuardService>(sp =>
{
    var peerRepo = sp.GetRequiredService<IRouterWireGuardPeerRepository>();
    var routerRepo = sp.GetRequiredService<IRouterRepository>();
    var vpnNetworkRepo = sp.GetRequiredService<IVpnNetworkRepository>();
    var vpnServiceClient = sp.GetRequiredService<Automais.Core.Interfaces.IVpnServiceClient>();
    var wireGuardSettings = sp.GetRequiredService<IOptions<WireGuardSettings>>();
    var logger = sp.GetService<ILogger<Automais.Core.Services.RouterWireGuardService>>();
    return new Automais.Core.Services.RouterWireGuardService(peerRepo, routerRepo, vpnNetworkRepo, wireGuardSettings, vpnServiceClient, logger);
});

builder.Services.AddScoped<IVpnNetworkService>(sp =>
{
    var tenantRepo = sp.GetRequiredService<ITenantRepository>();
    var vpnNetworkRepo = sp.GetRequiredService<IVpnNetworkRepository>();
    var deviceRepo = sp.GetRequiredService<IDeviceRepository>();
    var tenantUserService = sp.GetRequiredService<ITenantUserService>();
    var wireGuardSettings = sp.GetRequiredService<IOptions<WireGuardSettings>>();
    var vpnServiceClient = sp.GetService<Automais.Core.Interfaces.IVpnServiceClient>();
    var routerWg = sp.GetRequiredService<IRouterWireGuardService>();
    return new VpnNetworkService(tenantRepo, vpnNetworkRepo, deviceRepo, tenantUserService, wireGuardSettings, vpnServiceClient, routerWg);
});

// Registrar RouterService com RouterWireGuardService como dependência opcional
// RouterOsClient removido - comunicação RouterOS agora é feita via servidor VPN
builder.Services.AddScoped<IRouterService>(sp =>
{
    var routerRepo = sp.GetRequiredService<IRouterRepository>();
    var tenantRepo = sp.GetRequiredService<ITenantRepository>();
    var allowedNetworkRepo = sp.GetService<IRouterAllowedNetworkRepository>();
    var wireGuardService = sp.GetService<IRouterWireGuardService>(); // Opcional
    var vpnNetworkRepo = sp.GetService<IVpnNetworkRepository>(); // Opcional
    var logger = sp.GetService<ILogger<Automais.Core.Services.RouterService>>();
    return new Automais.Core.Services.RouterService(routerRepo, tenantRepo, allowedNetworkRepo, wireGuardService, vpnNetworkRepo, logger);
});

// Registrar RouterStaticRouteService
builder.Services.AddScoped<IRouterStaticRouteService>(sp =>
{
    var routeRepo = sp.GetRequiredService<IRouterStaticRouteRepository>();
    var routerRepo = sp.GetRequiredService<IRouterRepository>();
    var logger = sp.GetService<ILogger<Automais.Core.Services.RouterStaticRouteService>>();
    return new Automais.Core.Services.RouterStaticRouteService(routeRepo, routerRepo, logger);
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
/// Faz proxy de mensagens entre dois WebSockets
/// </summary>
static async Task ProxyWebSocketMessages(WebSocket source, WebSocket destination, CancellationToken cancellationToken, ILogger logger)
{
    var buffer = new byte[4096];
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

