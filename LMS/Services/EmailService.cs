using MailKit.Net.Smtp;
using MimeKit;

namespace LeadManagementSystem.Services;

/// <summary>
/// Email settings from appsettings.json
/// </summary>
public class EmailSettings
{
    public string SmtpServer { get; set; } = "smtp.gmail.com";
    public int SmtpPort { get; set; } = 587;
    public string SenderEmail { get; set; } = "noreply@yourdomain.com";
    public string SenderName { get; set; } = "Lead Management System";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public bool EnableSSL { get; set; } = true;
    public bool EnableEmailNotifications { get; set; } = true;
}

/// <summary>
/// Email notifications service interface
/// </summary>
public interface IEmailService
{
    Task SendPasswordResetEmailAsync(string email, string resetUrl, string userName);
    Task SendRegistrationConfirmationAsync(string email, string userName);
    Task SendPaymentReceiptAsync(string email, string clientRef, decimal amount, string paymentMode);
    Task SendFollowUpReminderAsync(string email, string hotelName, DateTime followupDate);
    Task SendStatusUpdateAsync(string email, string inquiryHotel, string newStatus);
}

/// <summary>
/// Email Service with SMTP implementation
/// Configured via appsettings.json EmailSettings section
/// Use environment variables or user-secrets in production for credentials
/// </summary>
public class EmailService : IEmailService
{
    private readonly ILogger<EmailService> _logger;
    private readonly EmailSettings _settings;

    public EmailService(ILogger<EmailService> logger, IConfiguration config)
    {
        _logger = logger;
        _settings = config.GetSection("EmailSettings").Get<EmailSettings>() ?? new EmailSettings();
    }

    public async Task SendPasswordResetEmailAsync(string email, string resetUrl, string userName)
    {
        if (!_settings.EnableEmailNotifications)
        {
            _logger.LogInformation($"[EMAIL DISABLED] Password reset for {email}");
            return;
        }

        try
        {
            var subject = "Password Reset Request - Lead Management System";
            var body = $@"
                <html>
                <body style='font-family: Arial, sans-serif;'>
                    <h2>Password Reset Request</h2>
                    <p>Hi {userName},</p>
                    <p>You requested to reset your password. Click the link below to proceed:</p>
                    <p><a href='{resetUrl}' style='background-color: #007bff; color: white; padding: 10px 20px; text-decoration: none; border-radius: 5px;'>Reset Password</a></p>
                    <p>This link expires in 24 hours.</p>
                    <p>If you didn't request this, ignore this email.</p>
                    <p>Best regards,<br/>Lead Management System</p>
                </body>
                </html>";

            await SendEmailAsync(email, subject, body);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to send password reset email to {email}: {ex.Message}");
        }
    }

    public async Task SendRegistrationConfirmationAsync(string email, string userName)
    {
        if (!_settings.EnableEmailNotifications)
        {
            _logger.LogInformation($"[EMAIL DISABLED] Registration confirmation for {email}");
            return;
        }

        try
        {
            var subject = "Welcome to Lead Management System";
            var body = $@"
                <html>
                <body style='font-family: Arial, sans-serif;'>
                    <h2>Welcome!</h2>
                    <p>Hi {userName},</p>
                    <p>Your account has been created successfully. You can now log in to the Lead Management System.</p>
                    <p>If you need to reset your password or have any questions, contact our support team.</p>
                    <p>Best regards,<br/>Lead Management System</p>
                </body>
                </html>";

            await SendEmailAsync(email, subject, body);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to send registration confirmation to {email}: {ex.Message}");
        }
    }

    public async Task SendPaymentReceiptAsync(string email, string clientRef, decimal amount, string paymentMode)
    {
        if (!_settings.EnableEmailNotifications)
        {
            _logger.LogInformation($"[EMAIL DISABLED] Payment receipt for {email}");
            return;
        }

        try
        {
            var subject = $"Payment Receipt - {clientRef}";
            var body = $@"
                <html>
                <body style='font-family: Arial, sans-serif;'>
                    <h2>Payment Receipt</h2>
                    <p>Thank you for your payment</p>
                    <table style='border-collapse: collapse; width: 100%; max-width: 500px;'>
                        <tr style='background-color: #f0f0f0;'>
                            <td style='padding: 10px; border: 1px solid #ddd;'><strong>Client Reference:</strong></td>
                            <td style='padding: 10px; border: 1px solid #ddd;'>{clientRef}</td>
                        </tr>
                        <tr>
                            <td style='padding: 10px; border: 1px solid #ddd;'><strong>Amount:</strong></td>
                            <td style='padding: 10px; border: 1px solid #ddd;'>₹{amount:F2}</td>
                        </tr>
                        <tr style='background-color: #f0f0f0;'>
                            <td style='padding: 10px; border: 1px solid #ddd;'><strong>Payment Mode:</strong></td>
                            <td style='padding: 10px; border: 1px solid #ddd;'>{paymentMode}</td>
                        </tr>
                        <tr>
                            <td style='padding: 10px; border: 1px solid #ddd;'><strong>Date:</strong></td>
                            <td style='padding: 10px; border: 1px solid #ddd;'>{DateTime.Now:yyyy-MM-dd HH:mm:ss}</td>
                        </tr>
                    </table>
                    <p>Best regards,<br/>Lead Management System</p>
                </body>
                </html>";

            await SendEmailAsync(email, subject, body);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to send payment receipt to {email}: {ex.Message}");
        }
    }

    public async Task SendFollowUpReminderAsync(string email, string hotelName, DateTime followupDate)
    {
        if (!_settings.EnableEmailNotifications)
        {
            _logger.LogInformation($"[EMAIL DISABLED] Follow-up reminder for {email}");
            return;
        }

        try
        {
            var subject = $"Follow-up Reminder - {hotelName}";
            var body = $@"
                <html>
                <body style='font-family: Arial, sans-serif;'>
                    <h2>Follow-up Reminder</h2>
                    <p>Hi,</p>
                    <p>This is a reminder to follow up with <strong>{hotelName}</strong></p>
                    <p><strong>Scheduled Follow-up Date:</strong> {followupDate:yyyy-MM-dd HH:mm:ss}</p>
                    <p>Please check your inquiry details for more information.</p>
                    <p>Best regards,<br/>Lead Management System</p>
                </body>
                </html>";

            await SendEmailAsync(email, subject, body);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to send follow-up reminder to {email}: {ex.Message}");
        }
    }

    public async Task SendStatusUpdateAsync(string email, string inquiryHotel, string newStatus)
    {
        if (!_settings.EnableEmailNotifications)
        {
            _logger.LogInformation($"[EMAIL DISABLED] Status update for {email}");
            return;
        }

        try
        {
            var subject = $"Inquiry Status Update - {inquiryHotel}";
            var body = $@"
                <html>
                <body style='font-family: Arial, sans-serif;'>
                    <h2>Inquiry Status Update</h2>
                    <p>Hi,</p>
                    <p>The status of your inquiry for <strong>{inquiryHotel}</strong> has been updated.</p>
                    <p><strong>New Status:</strong> {newStatus}</p>
                    <p>Please log in to the system for more details.</p>
                    <p>Best regards,<br/>Lead Management System</p>
                </body>
                </html>";

            await SendEmailAsync(email, subject, body);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to send status update to {email}: {ex.Message}");
        }
    }

    /// <summary>
    /// Core SMTP email sending method using MailKit
    /// More reliable than legacy SmtpClient for production use
    /// </summary>
    private async Task SendEmailAsync(string toEmail, string subject, string body)
    {
        if (string.IsNullOrWhiteSpace(_settings.Username) || string.IsNullOrWhiteSpace(_settings.Password))
        {
            _logger.LogWarning("Email credentials not configured. Email not sent (dev mode). To: " + toEmail);
            return;
        }

        try
        {
            using (var client = new SmtpClient())
            {
                await client.ConnectAsync(_settings.SmtpServer, _settings.SmtpPort, _settings.EnableSSL);
                await client.AuthenticateAsync(_settings.Username, _settings.Password);

                var message = new MimeMessage();
                message.From.Add(new MailboxAddress(_settings.SenderName, _settings.SenderEmail));
                message.To.Add(new MailboxAddress("", toEmail));
                message.Subject = subject;

                var builder = new BodyBuilder { HtmlBody = body };
                message.Body = builder.ToMessageBody();

                await client.SendAsync(message);
                await client.DisconnectAsync(true);
                
                _logger.LogInformation($"Email sent successfully to {toEmail}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to send email to {toEmail}: {ex.Message}");
            throw;
        }
    }
}
