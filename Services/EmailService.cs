using MailKit.Net.Smtp;
using MimeKit;
using MimeKit.Utils;
using CDM_OneServe_API.Models;

namespace CDM_OneServe_API.Services;

public class EmailService
{
    private readonly EmailSettings _settings;

    // Path to the logo file. Drop your logo at this location in the project
    // (e.g. wwwroot/assets/logo.png) — if it isn't found, emails simply fall
    // back to a text wordmark so sending never breaks.
    private static readonly string LogoPath =
        Path.Combine(AppContext.BaseDirectory, "wwwroot", "assets", "logo.png");

    public EmailService(EmailSettings settings)
    {
        _settings = settings;
    }

    public Task SendOtpAsync(
        string recipientEmail,
        string otpCode,
        string emailType = "verification")
    {
        bool isForgotPassword =
            emailType == "forgotpassword";

        var subject = isForgotPassword
            ? "CDM OneServe Password Reset"
            : "CDM OneServe OTP Verification";

        var heading = isForgotPassword
            ? "Password Reset Request"
            : "Email Verification";

        var intro = isForgotPassword
            ? "We received a request to reset your CDM OneServe password."
            : "Thank you for registering with CDM OneServe.";

        var instruction = isForgotPassword
            ? "Use the OTP below to reset your password."
            : "Use the OTP below to complete your account registration.";

        var ignoreMessage = isForgotPassword
            ? "If you did not request a password reset, you may safely ignore this email."
            : "If you did not request this verification code, you may safely ignore this email.";

        var bodyHtml = $@"
            <h2 style='color:#1F1F1F;margin-top:0;'>{heading}</h2>

            <p style='color:#555;line-height:1.8;'>{intro}</p>

            <p style='color:#555;line-height:1.8;'>{instruction}</p>

            <div style='
                background:#F4D35E;
                color:#1F1F1F;
                font-size:34px;
                font-weight:700;
                text-align:center;
                padding:20px;
                border-radius:14px;
                letter-spacing:8px;
                margin:30px 0;
            '>
                {otpCode}
            </div>

            <p style='color:#555;line-height:1.8;'>
                This OTP will expire in <strong>5 minutes</strong>.
            </p>

            <p style='color:#555;line-height:1.8;'>{ignoreMessage}</p>
        ";

        return SendTemplatedEmailAsync(
            recipientEmail,
            subject,
            "Integrated Campus Service Platform",
            bodyHtml);
    }

    public Task SendDigitalIdRequestConfirmationAsync(
        string recipientEmail,
        string fullName)
    {
        var bodyHtml = $@"
            <h2 style='color:#1F1F1F;margin-top:0;'>Request Submitted Successfully</h2>

            <p style='color:#555;line-height:1.8;'>
                Hello <strong>{fullName}</strong>,
            </p>

            <p style='color:#555;line-height:1.8;'>
                Your Digital ID request has been successfully submitted.
            </p>

            <div style='
                background:#F4D35E;
                color:#1F1F1F;
                text-align:center;
                padding:18px;
                border-radius:12px;
                margin:25px 0;
                font-weight:bold;
                font-size:20px;
            '>
                Status: Pending Review
            </div>

            <p style='color:#555;line-height:1.8;'>
                Our administrators will review your request and notify you once your
                Digital ID is approved and ready for use.
            </p>

            <p style='color:#555;line-height:1.8;'>
                Thank you for using CDM OneServe.
            </p>
        ";

        return SendTemplatedEmailAsync(
            recipientEmail,
            "CDM OneServe - Digital ID Request Submitted",
            "Digital ID Request",
            bodyHtml);
    }

    public Task SendDigitalIdApprovedAsync(
        string recipientEmail,
        string fullName)
    {
        var bodyHtml = $@"
            <h2 style='color:#1F1F1F;margin-top:0;'>Digital ID Approved</h2>

            <p style='color:#555;line-height:1.8;'>
                Hello <strong>{fullName}</strong>,
            </p>

            <p style='color:#555;line-height:1.8;'>
                Your Digital ID request has been approved successfully.
            </p>

            <div style='
                background:#D7F0E1;
                color:#106A2E;
                text-align:center;
                padding:18px;
                border-radius:12px;
                margin:25px 0;
                font-weight:bold;
                font-size:20px;
            '>
                Status: Approved
            </div>

            <p style='color:#555;line-height:1.8;'>
                You may now access your Digital ID through the CDM OneServe portal.
            </p>

            <p style='color:#555;line-height:1.8;'>
                Thank you for using CDM OneServe.
            </p>
        ";

        return SendTemplatedEmailAsync(
            recipientEmail,
            "CDM OneServe - Digital ID Approved",
            "Digital ID Request",
            bodyHtml);
    }

    public Task SendDigitalIdRejectedAsync(
        string recipientEmail,
        string fullName)
    {
        var bodyHtml = $@"
            <h2 style='color:#1F1F1F;margin-top:0;'>Digital ID Request Rejected</h2>

            <p style='color:#555;line-height:1.8;'>
                Hello <strong>{fullName}</strong>,
            </p>

            <p style='color:#555;line-height:1.8;'>
                Your Digital ID request has been rejected by the administrator.
            </p>

            <div style='
                background:#FBDADA;
                color:#A6342F;
                text-align:center;
                padding:18px;
                border-radius:12px;
                margin:25px 0;
                font-weight:bold;
                font-size:20px;
            '>
                Status: Rejected
            </div>

            <p style='color:#555;line-height:1.8;'>
                Please review your submitted information and submit a new request.
            </p>

            <p style='color:#555;line-height:1.8;'>
                Thank you for using CDM OneServe.
            </p>
        ";

        return SendTemplatedEmailAsync(
            recipientEmail,
            "CDM OneServe - Digital ID Request Rejected",
            "Digital ID Request",
            bodyHtml);
    }

    // ==========================================
    // ADMIN ALERT — NEW DIGITAL ID REQUEST
    // ==========================================
    public Task SendNewDigitalIdRequestAdminAlertAsync(
        string adminEmail,
        string requesterName)
    {
        var bodyHtml = $@"
            <h2 style='color:#1F1F1F;margin-top:0;'>
                New Digital ID Request
            </h2>

            <p style='color:#555;line-height:1.8;'>
                A new Digital ID request has been submitted and is
                waiting for review.
            </p>

            <div style='
                background:#F4D35E;
                color:#1F1F1F;
                padding:18px;
                border-radius:12px;
                margin:25px 0;
                font-weight:bold;
                font-size:18px;
            '>
                {requesterName} submitted a new Digital ID request.
            </div>

            <p style='color:#555;line-height:1.8;'>
                Please log in to the CDM OneServe Admin Portal to
                review the request.
            </p>
        ";

        return SendTemplatedEmailAsync(
            adminEmail,
            "CDM OneServe - New Digital ID Request",
            "Admin Notification",
            bodyHtml);
    }


    // ==========================================
    // ADMIN ALERT — PENDING REQUEST THRESHOLD
    // ==========================================
    public Task SendPendingDigitalIdThresholdAlertAsync(
        string adminEmail,
        int pendingCount)
    {
        var bodyHtml = $@"
            <h2 style='color:#1F1F1F;margin-top:0;'>
                Pending Digital ID Requests
            </h2>

            <p style='color:#555;line-height:1.8;'>
                The number of pending Digital ID requests has reached
                the configured notification threshold.
            </p>

            <div style='
                background:#F4D35E;
                color:#1F1F1F;
                text-align:center;
                padding:20px;
                border-radius:12px;
                margin:25px 0;
                font-weight:bold;
                font-size:20px;
            '>
                {pendingCount} Pending Requests
            </div>

            <p style='color:#555;line-height:1.8;'>
                There are now <strong>{pendingCount}</strong> pending
                Digital ID requests waiting for review.
            </p>

            <p style='color:#555;line-height:1.8;'>
                Please log in to the CDM OneServe Admin Portal to
                review the pending requests.
            </p>
        ";

        return SendTemplatedEmailAsync(
            adminEmail,
            "CDM OneServe - Pending Digital ID Request Threshold",
            "Admin Notification",
            bodyHtml);
    }



    /// <summary>
    /// Builds the shared CDM OneServe email shell (header with logo, content
    /// area, footer) around the given body HTML, and sends it. Every email
    /// in this service routes through here so all messages share one look.
    /// </summary>
    private async Task SendTemplatedEmailAsync(
        string recipientEmail,
        string subject,
        string headerTagline,
        string bodyHtml)
    {
        var message = new MimeMessage();

        message.From.Add(
            MailboxAddress.Parse(_settings.Email));

        message.To.Add(
            MailboxAddress.Parse(recipientEmail));

        message.Subject = subject;

        var builder = new BodyBuilder();

        string logoHtml;

        if (File.Exists(LogoPath))
        {
            var logoImage = builder.LinkedResources.Add(LogoPath);
            logoImage.ContentId = MimeUtils.GenerateMessageId();

            logoHtml = $@"
                <img
                    src='cid:{logoImage.ContentId}'
                    alt='CDM OneServe'
                    style='height:48px;display:block;margin:0 auto 12px;'
                />";
        }
        else
        {
            // No logo file found on disk — fall back to a text wordmark so
            // the email still sends instead of throwing.
            logoHtml = "";
        }

        builder.HtmlBody = $@"
            <!DOCTYPE html>
            <html>
            <head>
            <meta charset='UTF-8'>
            <title>{subject}</title>
            </head>

            <body style='
                margin:0;
                padding:0;
                background:#F1F1F1;
                font-family:Segoe UI, Arial, sans-serif;
            '>

            <table width='100%' cellpadding='0' cellspacing='0'>
            <tr>
            <td align='center' style='padding:40px 20px;'>

            <table
                width='600'
                cellpadding='0'
                cellspacing='0'
                style='
                    background:#FFFFFF;
                    border-radius:18px;
                    overflow:hidden;
                    box-shadow:0 8px 24px rgba(0,0,0,.08);
                '
            >

            <tr>
            <td
                align='center'
                style='
                    background:#106A2E;
                    padding:32px;
                '
            >
                {logoHtml}

                <h1 style='
                    color:white;
                    margin:0;
                    font-size:28px;
                '>
                    CDM OneServe
                </h1>

                <p style='
                    color:#F1F1F1;
                    margin-top:8px;
                    margin-bottom:0;
                '>
                    {headerTagline}
                </p>

            </td>
            </tr>

            <tr>
            <td style='padding:40px;'>

                {bodyHtml}

            </td>
            </tr>

            <tr>
            <td
                align='center'
                style='
                    background:#0D7856;
                    color:white;
                    padding:24px;
                    font-size:13px;
                '
            >
                © 2026 CDM OneServe<br/>
                Colegio de Montalban
            </td>
            </tr>

            </table>

            </td>
            </tr>
            </table>

            </body>
            </html>";

        message.Body = builder.ToMessageBody();

        using var smtp = new SmtpClient();

        await smtp.ConnectAsync(
            _settings.Host,
            _settings.Port,
            MailKit.Security.SecureSocketOptions.StartTls);

        await smtp.AuthenticateAsync(
            _settings.Email,
            _settings.Password);

        await smtp.SendAsync(message);

        await smtp.DisconnectAsync(true);
    }
}