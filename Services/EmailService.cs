using MailKit.Net.Smtp;
using MimeKit;
using MimeKit.Utils;
using CDM_OneServe_API.Models;

namespace CDM_OneServe_API.Services;

public class EmailService
{
    private readonly EmailSettings _settings;

    private static readonly string LogoPath =
        Path.Combine(
            AppContext.BaseDirectory,
            "wwwroot",
            "assets",
            "logo.png"
        );


    public EmailService(EmailSettings settings)
    {
        _settings = settings;
    }


    // =========================================================
    // OTP
    // =========================================================

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
            <h2 style='color:#1F1F1F;margin-top:0;'>
                {heading}
            </h2>

            <p style='color:#555;line-height:1.8;'>
                {intro}
            </p>

            <p style='color:#555;line-height:1.8;'>
                {instruction}
            </p>

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

            <p style='color:#555;line-height:1.8;'>
                {ignoreMessage}
            </p>
        ";

        return SendTemplatedEmailAsync(
            recipientEmail,
            subject,
            "Integrated Campus Service Platform",
            bodyHtml
        );
    }


    // =========================================================
    // ADMIN — NEW PHYSICAL ID REQUEST
    // =========================================================

    public Task SendNewPhysicalIdRequestAdminAlertAsync(
        string adminEmail,
        string requesterName)
    {
        var bodyHtml = $@"
            <h2 style='color:#1F1F1F;margin-top:0;'>
                New Physical ID Verification Request
            </h2>

            <p style='color:#555;line-height:1.8;'>
                A new Physical ID verification request has been
                submitted and is waiting for review.
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
                {requesterName} submitted a Physical ID
                for verification.
            </div>

            <p style='color:#555;line-height:1.8;'>
                Please log in to the CDM OneServe Admin Portal
                to review the submitted Physical ID.
            </p>
        ";

        return SendTemplatedEmailAsync(
            adminEmail,
            "CDM OneServe - New Physical ID Verification Request",
            "Physical ID Verification",
            bodyHtml
        );
    }


    // =========================================================
    // ADMIN — THRESHOLD
    // =========================================================

    public Task SendPhysicalIdThresholdAlertAsync(
        string adminEmail,
        int pendingCount,
        int threshold)
    {
        var bodyHtml = $@"
            <h2 style='color:#1F1F1F;margin-top:0;'>
                Pending Physical ID Verifications
            </h2>

            <p style='color:#555;line-height:1.8;'>
                The number of pending Physical ID verification
                requests has reached the configured notification
                threshold.
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
                {pendingCount} Pending Verification Request(s)
            </div>

            <p style='color:#555;line-height:1.8;'>
                Your configured threshold is
                <strong>{threshold}</strong>
                pending request(s).
            </p>

            <p style='color:#555;line-height:1.8;'>
                There are currently
                <strong>{pendingCount}</strong>
                Physical ID verification request(s)
                waiting for review.
            </p>

            <p style='color:#555;line-height:1.8;'>
                Please log in to the CDM OneServe Admin Portal
                to review the pending submissions.
            </p>
        ";

        return SendTemplatedEmailAsync(
            adminEmail,
            "CDM OneServe - Physical ID Verification Threshold Reached",
            "Admin Notification",
            bodyHtml
        );
    }


    // =========================================================
    // USER — PHYSICAL ID RE-UPLOAD
    // =========================================================

    public Task SendPhysicalIdReuploadRequestAsync(
        string recipientEmail,
        string fullName)
    {
        var bodyHtml = $@"
            <h2 style='color:#1F1F1F;margin-top:0;'>
                Physical ID Re-upload Required
            </h2>

            <p style='color:#555;line-height:1.8;'>
                Hello <strong>{fullName}</strong>,
            </p>

            <p style='color:#555;line-height:1.8;'>
                Your submitted Physical ID could not be verified
                and needs to be uploaded again.
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
                Status: Re-upload Required
            </div>

            <p style='color:#555;line-height:1.8;'>
                Please log in to your CDM OneServe account and
                upload a clear and valid copy of your Physical ID.
            </p>

            <p style='color:#555;line-height:1.8;'>
                Thank you for using CDM OneServe.
            </p>
        ";

        return SendTemplatedEmailAsync(
            recipientEmail,
            "CDM OneServe - Physical ID Re-upload Required",
            "Physical ID Verification",
            bodyHtml
        );
    }


    // =========================================================
    // USER — PHYSICAL ID REJECTED
    // =========================================================

    public Task SendPhysicalIdRejectedAsync(
        string recipientEmail,
        string fullName)
    {
        var bodyHtml = $@"
            <h2 style='color:#1F1F1F;margin-top:0;'>
                Physical ID Verification Rejected
            </h2>

            <p style='color:#555;line-height:1.8;'>
                Hello <strong>{fullName}</strong>,
            </p>

            <p style='color:#555;line-height:1.8;'>
                Your Physical ID verification request has been
                rejected by the administrator.
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
                Please contact the administration if you need
                further information regarding your verification.
            </p>

            <p style='color:#555;line-height:1.8;'>
                Thank you for using CDM OneServe.
            </p>
        ";

        return SendTemplatedEmailAsync(
            recipientEmail,
            "CDM OneServe - Physical ID Verification Rejected",
            "Physical ID Verification",
            bodyHtml
        );
    }


    // =========================================================
    // USER — PHYSICAL ID APPROVED
    // =========================================================

    public Task SendPhysicalIdApprovedAsync(
        string recipientEmail,
        string fullName)
    {
        var bodyHtml = $@"
            <h2 style='color:#1F1F1F;margin-top:0;'>
                Physical ID Verification Approved
            </h2>

            <p style='color:#555;line-height:1.8;'>
                Hello <strong>{fullName}</strong>,
            </p>

            <p style='color:#555;line-height:1.8;'>
                Your Physical ID has been successfully verified
                by the administrator.
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
                Your CDM OneServe account is now approved.
            </p>

            <p style='color:#555;line-height:1.8;'>
                Thank you for using CDM OneServe.
            </p>
        ";

        return SendTemplatedEmailAsync(
            recipientEmail,
            "CDM OneServe - Physical ID Verification Approved",
            "Physical ID Verification",
            bodyHtml
        );
    }


    // =========================================================
    // SHARED EMAIL TEMPLATE
    // =========================================================

    private async Task SendTemplatedEmailAsync(
        string recipientEmail,
        string subject,
        string headerTagline,
        string bodyHtml)
    {
        var message = new MimeMessage();

        message.From.Add(
            MailboxAddress.Parse(_settings.Email)
        );

        message.To.Add(
            MailboxAddress.Parse(recipientEmail)
        );

        message.Subject = subject;

        var builder = new BodyBuilder();

        string logoHtml;

        if (File.Exists(LogoPath))
        {
            var logoImage =
                builder.LinkedResources.Add(LogoPath);

            logoImage.ContentId =
                MimeUtils.GenerateMessageId();

            logoHtml = $@"
                <img
                    src='cid:{logoImage.ContentId}'
                    alt='CDM OneServe'
                    style='
                        height:48px;
                        display:block;
                        margin:0 auto 12px;
                    '
                />";
        }
        else
        {
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

                <table
                    width='100%'
                    cellpadding='0'
                    cellspacing='0'
                >
                    <tr>

                        <td
                            align='center'
                            style='padding:40px 20px;'
                        >

                            <table
                                width='600'
                                cellpadding='0'
                                cellspacing='0'
                                style='
                                    background:#FFFFFF;
                                    border-radius:18px;
                                    overflow:hidden;
                                    box-shadow:
                                        0 8px 24px
                                        rgba(0,0,0,.08);
                                '
                            >

                                <!-- HEADER -->

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


                                <!-- CONTENT -->

                                <tr>

                                    <td style='padding:40px;'>
                                        {bodyHtml}
                                    </td>

                                </tr>


                                <!-- FOOTER -->

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


        message.Body =
            builder.ToMessageBody();


        using var smtp = new SmtpClient();


        await smtp.ConnectAsync(
            _settings.Host,
            _settings.Port,
            MailKit.Security.SecureSocketOptions.StartTls
        );


        await smtp.AuthenticateAsync(
            _settings.Email,
            _settings.Password
        );


        await smtp.SendAsync(message);


        await smtp.DisconnectAsync(true);
    }
}