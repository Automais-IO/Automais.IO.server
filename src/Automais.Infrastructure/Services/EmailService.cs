using System.Linq;
using System.Net;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Automais.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Automais.Infrastructure.Services;

public class EmailService : IEmailService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmailService>? _logger;
    private readonly string? _smtpHost;
    private readonly int _smtpPort;
    private readonly string? _smtpUsername;
    private readonly string? _smtpPassword;
    private readonly bool _smtpUseSsl;
    private readonly string? _fromEmail;
    private readonly string? _fromName;

    public EmailService(IConfiguration configuration, ILogger<EmailService>? logger = null)
    {
        _configuration = configuration;
        _logger = logger;
        
        // Office365 SMTP Configuration
        _smtpHost = _configuration["Email:Smtp:Host"] ?? Environment.GetEnvironmentVariable("SMTP_HOST") ?? "smtp.office365.com";
        // Porta 587 com STARTTLS (como no PowerShell que funcionou)
        var portConfig = _configuration["Email:Smtp:Port"] ?? Environment.GetEnvironmentVariable("SMTP_PORT");
        _smtpPort = portConfig != null ? int.Parse(portConfig) : 587;
        _smtpUsername = _configuration["Email:Smtp:Username"] ?? Environment.GetEnvironmentVariable("SMTP_USERNAME") ?? "noreply@automais.io";
        
        // Ler senha - priorizar variável de ambiente, ignorar se for placeholder do appsettings
        var passwordFromConfig = _configuration["Email:Smtp:Password"];
        if (string.IsNullOrWhiteSpace(passwordFromConfig) || passwordFromConfig.StartsWith("${") && passwordFromConfig.EndsWith("}"))
        {
            // Se for placeholder ou vazio, ler da variável de ambiente
            _smtpPassword = Environment.GetEnvironmentVariable("SMTP_PASSWORD") ?? Environment.GetEnvironmentVariable("EMAIL_PASSWORD");
        }
        else
        {
            _smtpPassword = passwordFromConfig;
        }
        
        // Remover espaços em branco da senha (pode ter sido adicionado acidentalmente)
        if (!string.IsNullOrWhiteSpace(_smtpPassword))
        {
            _smtpPassword = _smtpPassword.Trim();
        }
        
        _smtpUseSsl = bool.Parse(_configuration["Email:Smtp:UseSsl"] ?? Environment.GetEnvironmentVariable("SMTP_USE_SSL") ?? "true");
        _fromEmail = _configuration["Email:From:Address"] ?? Environment.GetEnvironmentVariable("EMAIL_FROM_ADDRESS") ?? "noreply@automais.io";
        _fromName = _configuration["Email:From:Name"] ?? Environment.GetEnvironmentVariable("EMAIL_FROM_NAME") ?? "Automais.IO";

        // Apenas Debug: evita ruído no journald e não expõe dados sensíveis em Information
        _logger?.LogDebug(
            "SMTP configurado - Host: {Host}, Port: {Port}, User: {Username}, senha OK: {HasPassword}, SSL: {Ssl}, From: {From}",
            _smtpHost, _smtpPort, _smtpUsername, !string.IsNullOrWhiteSpace(_smtpPassword), _smtpUseSsl, _fromEmail);

        _logger?.LogDebug(
            "SMTP origem - Host: {Host} ({HostSrc}), User: {User} ({UserSrc}), senha: {PassSrc}",
            _smtpHost,
            _configuration["Email:Smtp:Host"] != null ? "appsettings" : (Environment.GetEnvironmentVariable("SMTP_HOST") != null ? "env" : "default"),
            _smtpUsername,
            _configuration["Email:Smtp:Username"] != null ? "appsettings" : (Environment.GetEnvironmentVariable("SMTP_USERNAME") != null ? "env" : "default"),
            _configuration["Email:Smtp:Password"] != null ? "appsettings" : (Environment.GetEnvironmentVariable("SMTP_PASSWORD") != null || Environment.GetEnvironmentVariable("EMAIL_PASSWORD") != null ? "env" : "não configurado"));
    }

    public async Task SendWelcomeEmailAsync(string toEmail, string toName, string temporaryPassword, CancellationToken cancellationToken = default)
    {
        var subject = "Bem-vindo à Automais.IO";
        var body = GetWelcomeEmailTemplate(toName, temporaryPassword);
        
        await SendEmailAsync(toEmail, toName, subject, body, cancellationToken);
    }

    public async Task SendPasswordResetEmailAsync(string toEmail, string toName, string temporaryPassword, CancellationToken cancellationToken = default)
    {
        var subject = "Nova senha temporária - Automais.IO";
        var body = GetPasswordResetEmailTemplate(toName, temporaryPassword);
        
        await SendEmailAsync(toEmail, toName, subject, body, cancellationToken);
    }

    private async Task SendEmailAsync(string toEmail, string toName, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        // Verificação detalhada da configuração
        var configIssues = new List<string>();
        if (string.IsNullOrWhiteSpace(_smtpHost))
            configIssues.Add("SMTP Host não configurado");
        if (string.IsNullOrWhiteSpace(_smtpUsername))
            configIssues.Add("SMTP Username não configurado");
        if (string.IsNullOrWhiteSpace(_smtpPassword))
            configIssues.Add("SMTP Password não configurada (verifique EMAIL_PASSWORD ou SMTP_PASSWORD)");
        if (string.IsNullOrWhiteSpace(_fromEmail))
            configIssues.Add("Email de origem não configurado");

        if (configIssues.Any())
        {
            var errorMsg = $"Configuração de email incompleta: {string.Join(", ", configIssues)}";
            _logger?.LogError(errorMsg);
            _logger?.LogInformation("Configuração atual - Host: {Host}, Port: {Port}, Username: {Username}, Password configurada: {HasPassword}, From: {From}", 
                _smtpHost ?? "null", _smtpPort, _smtpUsername ?? "null", !string.IsNullOrWhiteSpace(_smtpPassword), _fromEmail ?? "null");
            throw new InvalidOperationException(errorMsg);
        }

        try
        {
            // Log da configuração (sem expor senha)
            _logger?.LogDebug("Tentando enviar email para {Email} via {Host}:{Port}", toEmail, _smtpHost, _smtpPort);

            // Usar MailKit que tem melhor suporte para Office365 e STARTTLS
            using var client = new SmtpClient();
            
            _logger?.LogDebug("Conectando ao servidor SMTP {Host}:{Port} com STARTTLS", _smtpHost, _smtpPort);
            
            // Conectar ao servidor SMTP com STARTTLS (Office365 requer na porta 587)
            await client.ConnectAsync(_smtpHost, _smtpPort, SecureSocketOptions.StartTls, cancellationToken);
            
            _logger?.LogDebug("Conectado. Tentando autenticar com usuário {Username}", _smtpUsername);
            
            // Autenticar
            await client.AuthenticateAsync(_smtpUsername, _smtpPassword, cancellationToken);
            
            _logger?.LogDebug("Autenticado com sucesso");

            _logger?.LogDebug("Conectado e autenticado no servidor SMTP");

            // Criar mensagem usando MimeKit
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_fromName, _fromEmail));
            message.To.Add(new MailboxAddress(toName, toEmail));
            message.Subject = subject;

            var bodyBuilder = new BodyBuilder
            {
                HtmlBody = htmlBody
            };
            message.Body = bodyBuilder.ToMessageBody();

            _logger?.LogDebug("Enviando email - De: {From} ({FromName}) Para: {To} ({ToName}), Assunto: {Subject}", 
                _fromEmail, _fromName, toEmail, toName, subject);

            // Enviar email
            await client.SendAsync(message, cancellationToken);
            
            // Desconectar
            await client.DisconnectAsync(true, cancellationToken);
            
            _logger?.LogInformation("Email enviado com sucesso para {Email}", toEmail);
        }
        catch (MailKit.Security.AuthenticationException authEx)
        {
            _logger?.LogError(authEx, "Erro de autenticação SMTP ao enviar email para {Email}. Mensagem: {Message}", 
                toEmail, authEx.Message);
            
            _logger?.LogError("SMTP após falha de auth - Host: {Host}, Port: {Port}, Username: {Username}, senha definida: {HasPassword}",
                _smtpHost, _smtpPort, _smtpUsername, !string.IsNullOrWhiteSpace(_smtpPassword));
            
            throw new InvalidOperationException($"Erro de autenticação SMTP: {authEx.Message}. Verifique as credenciais.", authEx);
        }
        catch (MailKit.Net.Smtp.SmtpCommandException smtpEx)
        {
            _logger?.LogError(smtpEx, "Erro SMTP ao enviar email para {Email}. StatusCode: {StatusCode}, Message: {Message}", 
                toEmail, smtpEx.StatusCode, smtpEx.Message);
            
            throw new InvalidOperationException($"Erro SMTP ({smtpEx.StatusCode}): {smtpEx.Message}", smtpEx);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Erro inesperado ao enviar email para {Email}. Tipo: {ExceptionType}, Mensagem: {Message}", 
                toEmail, ex.GetType().Name, ex.Message);
            throw new InvalidOperationException($"Erro ao enviar email: {ex.Message} (Tipo: {ex.GetType().Name})", ex);
        }
    }

    private static string GetWelcomeEmailTemplate(string name, string temporaryPassword)
    {
        return $@"
<!DOCTYPE html>
<html lang=""pt-BR"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Bem-vindo à Automais.IO</title>
</head>
<body style=""margin: 0; padding: 0; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif; background-color: #f5f5f5;"">
    <table role=""presentation"" style=""width: 100%; border-collapse: collapse;"">
        <tr>
            <td style=""padding: 40px 20px; text-align: center;"">
                <table role=""presentation"" style=""max-width: 600px; margin: 0 auto; background-color: #ffffff; border-radius: 8px; box-shadow: 0 2px 4px rgba(0,0,0,0.1);"">
                    <tr>
                        <td style=""padding: 40px 30px; text-align: center; background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); border-radius: 8px 8px 0 0;"">
                            <h1 style=""margin: 0; color: #ffffff; font-size: 28px; font-weight: 600;"">Bem-vindo à Automais.IO</h1>
                        </td>
                    </tr>
                    <tr>
                        <td style=""padding: 40px 30px;"">
                            <p style=""margin: 0 0 20px 0; color: #333333; font-size: 16px; line-height: 1.6;"">
                                Olá <strong>{name}</strong>,
                            </p>
                            <p style=""margin: 0 0 20px 0; color: #333333; font-size: 16px; line-height: 1.6;"">
                                Sua conta foi criada com sucesso na plataforma Automais.IO!
                            </p>
                            <p style=""margin: 0 0 20px 0; color: #333333; font-size: 16px; line-height: 1.6;"">
                                Sua senha temporária é:
                            </p>
                            <div style=""background-color: #f8f9fa; border: 2px solid #667eea; border-radius: 6px; padding: 20px; margin: 20px 0; text-align: center;"">
                                <p style=""margin: 0; font-size: 24px; font-weight: 600; color: #667eea; font-family: 'Courier New', monospace; letter-spacing: 2px;"">
                                    {temporaryPassword}
                                </p>
                            </div>
                            <div style=""margin: 20px 0; padding: 15px; background-color: #fff3cd; border-left: 4px solid #ffc107; border-radius: 4px;"">
                                <p style=""margin: 0; color: #856404; font-size: 14px; line-height: 1.6;"">
                                    <strong>⚠️ Atenção:</strong> Esta senha temporária é válida por <strong>12 horas</strong>. Após esse período, ela não funcionará mais e você precisará solicitar uma nova senha.
                                </p>
                            </div>
                            <p style=""margin: 20px 0; color: #666666; font-size: 14px; line-height: 1.6;"">
                                <strong>Importante:</strong> Por segurança, recomendamos que você altere esta senha no primeiro acesso.
                            </p>
                            <div style=""margin: 30px 0; padding: 20px; background-color: #f0f4ff; border-left: 4px solid #667eea; border-radius: 4px;"">
                                <p style=""margin: 0; color: #333333; font-size: 14px; line-height: 1.6;"">
                                    <strong>Dicas de segurança:</strong>
                                </p>
                                <ul style=""margin: 10px 0 0 0; padding-left: 20px; color: #666666; font-size: 14px; line-height: 1.8;"">
                                    <li>Use uma senha forte com pelo menos 8 caracteres</li>
                                    <li>Combine letras maiúsculas, minúsculas, números e símbolos</li>
                                    <li>Não compartilhe sua senha com ninguém</li>
                                </ul>
                            </div>
                        </td>
                    </tr>
                    <tr>
                        <td style=""padding: 30px; text-align: center; background-color: #f8f9fa; border-radius: 0 0 8px 8px;"">
                            <p style=""margin: 0; color: #666666; font-size: 12px; line-height: 1.6;"">
                                Se você não solicitou esta conta, por favor, ignore este email.
                            </p>
                            <p style=""margin: 10px 0 0 0; color: #999999; font-size: 12px;"">
                                © {DateTime.Now.Year} Automais.IO - Todos os direitos reservados
                            </p>
                        </td>
                    </tr>
                </table>
            </td>
        </tr>
    </table>
</body>
</html>";
    }

    private static string GetPasswordResetEmailTemplate(string name, string temporaryPassword)
    {
        return $@"
<!DOCTYPE html>
<html lang=""pt-BR"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Nova senha temporária - Automais.IO</title>
</head>
<body style=""margin: 0; padding: 0; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif; background-color: #f5f5f5;"">
    <table role=""presentation"" style=""width: 100%; border-collapse: collapse;"">
        <tr>
            <td style=""padding: 40px 20px; text-align: center;"">
                <table role=""presentation"" style=""max-width: 600px; margin: 0 auto; background-color: #ffffff; border-radius: 8px; box-shadow: 0 2px 4px rgba(0,0,0,0.1);"">
                    <tr>
                        <td style=""padding: 40px 30px; text-align: center; background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); border-radius: 8px 8px 0 0;"">
                            <h1 style=""margin: 0; color: #ffffff; font-size: 28px; font-weight: 600;"">Nova Senha Temporária</h1>
                        </td>
                    </tr>
                    <tr>
                        <td style=""padding: 40px 30px;"">
                            <p style=""margin: 0 0 20px 0; color: #333333; font-size: 16px; line-height: 1.6;"">
                                Olá <strong>{name}</strong>,
                            </p>
                            <p style=""margin: 0 0 20px 0; color: #333333; font-size: 16px; line-height: 1.6;"">
                                Sua senha foi resetada com sucesso. Uma nova senha temporária foi gerada para você:
                            </p>
                            <div style=""background-color: #f8f9fa; border: 2px solid #667eea; border-radius: 6px; padding: 20px; margin: 20px 0; text-align: center;"">
                                <p style=""margin: 0; font-size: 24px; font-weight: 600; color: #667eea; font-family: 'Courier New', monospace; letter-spacing: 2px;"">
                                    {temporaryPassword}
                                </p>
                            </div>
                            <div style=""margin: 20px 0; padding: 15px; background-color: #fff3cd; border-left: 4px solid #ffc107; border-radius: 4px;"">
                                <p style=""margin: 0; color: #856404; font-size: 14px; line-height: 1.6;"">
                                    <strong>⚠️ Atenção:</strong> Esta senha temporária é válida por <strong>12 horas</strong>. Após esse período, ela não funcionará mais e você precisará solicitar uma nova senha.
                                </p>
                            </div>
                            <p style=""margin: 20px 0; color: #666666; font-size: 14px; line-height: 1.6;"">
                                <strong>Importante:</strong> Por segurança, recomendamos que você altere esta senha no próximo acesso.
                            </p>
                            <div style=""margin: 30px 0; padding: 20px; background-color: #fff3cd; border-left: 4px solid #ffc107; border-radius: 4px;"">
                                <p style=""margin: 0; color: #856404; font-size: 14px; line-height: 1.6;"">
                                    <strong>⚠️ Atenção:</strong> Se você não solicitou o reset de senha, entre em contato com o suporte imediatamente.
                                </p>
                            </div>
                        </td>
                    </tr>
                    <tr>
                        <td style=""padding: 30px; text-align: center; background-color: #f8f9fa; border-radius: 0 0 8px 8px;"">
                            <p style=""margin: 0; color: #666666; font-size: 12px; line-height: 1.6;"">
                                Este é um email automático, por favor não responda.
                            </p>
                            <p style=""margin: 10px 0 0 0; color: #999999; font-size: 12px;"">
                                © {DateTime.Now.Year} Automais.IO - Todos os direitos reservados
                            </p>
                        </td>
                    </tr>
                </table>
            </td>
        </tr>
    </table>
</body>
</html>";
    }
}

