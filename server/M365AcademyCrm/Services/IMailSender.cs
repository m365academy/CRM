namespace M365AcademyCrm.Services;

public interface IMailSender
{
    /// <summary>
    /// Sends a plain-text email from Crm:MailFromAddress. Throws on failure -
    /// callers decide whether that's fatal (it is for "send email to
    /// contact", since the user is waiting to see it went).
    /// </summary>
    Task SendAsync(string toAddress, string? toName, string subject, string body, string? replyToAddress, string? replyToName, CancellationToken cancellationToken);
}
