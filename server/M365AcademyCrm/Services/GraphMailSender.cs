using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Core;
using Azure.Identity;
using M365AcademyCrm.Models;
using Microsoft.Extensions.Options;

namespace M365AcademyCrm.Services;

/// <summary>
/// Sends CRM email (to contacts/leads, and quotes) via the Microsoft Graph
/// sendMail API as Crm:MailFromAddress, authenticating as the CRM app
/// registration (client credentials). Same approach as the website's
/// GraphEmailSender - the app registration needs Mail.Send (application),
/// ideally restricted to the one mailbox with an ApplicationAccessPolicy.
/// Every message is BCCed to the sending mailbox so there's a copy outside
/// the CRM, and saved to Sent Items.
/// </summary>
public class GraphMailSender : IMailSender
{
    private static readonly string[] GraphScope = ["https://graph.microsoft.com/.default"];
    // WhenWritingNull: sendMail rejects an explicit "replyTo": null - it must be omitted.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IConfiguration _configuration;
    private readonly CrmOptions _options;
    private readonly HttpClient _httpClient;
    private readonly ILogger<GraphMailSender> _logger;

    public GraphMailSender(IConfiguration configuration, IOptions<CrmOptions> options, HttpClient httpClient, ILogger<GraphMailSender> logger)
    {
        _configuration = configuration;
        _options = options.Value;
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task SendAsync(string toAddress, string? toName, string subject, string body, string? replyToAddress, string? replyToName, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));
        var timeoutToken = timeoutCts.Token;

        AccessToken token;
        try
        {
            var credential = new ClientSecretCredential(
                _configuration["AzureAd:TenantId"] ?? string.Empty,
                _configuration["AzureAd:ClientId"] ?? string.Empty,
                _configuration["AzureAd:ClientSecret"] ?? string.Empty);
            token = await credential.GetTokenAsync(new TokenRequestContext(GraphScope), timeoutToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Timed out authenticating with Microsoft Graph (Entra ID app registration).");
        }

        var message = new GraphMessage
        {
            Subject = subject,
            Body = new GraphItemBody { ContentType = "Text", Content = body },
            ToRecipients = [new GraphRecipient { EmailAddress = new GraphEmailAddress { Address = toAddress, Name = toName } }],
            BccRecipients = [new GraphRecipient { EmailAddress = new GraphEmailAddress { Address = _options.MailFromAddress } }],
        };

        if (!string.IsNullOrEmpty(replyToAddress))
        {
            message.ReplyTo = [new GraphRecipient { EmailAddress = new GraphEmailAddress { Address = replyToAddress, Name = replyToName } }];
        }

        var payload = new GraphSendMailRequest { Message = message, SaveToSentItems = true };

        using var content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(_options.MailFromAddress)}/sendMail")
        {
            Content = content,
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(httpRequest, timeoutToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Timed out calling the Microsoft Graph sendMail API.");
        }

        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Graph sendMail failed with {StatusCode}: {Body}", response.StatusCode, responseBody);
            throw new HttpRequestException($"Microsoft Graph sendMail returned {(int)response.StatusCode} {response.StatusCode}.");
        }
    }

    private class GraphSendMailRequest
    {
        public required GraphMessage Message { get; set; }
        public bool SaveToSentItems { get; set; }
    }

    private class GraphMessage
    {
        public required string Subject { get; set; }
        public required GraphItemBody Body { get; set; }
        public required List<GraphRecipient> ToRecipients { get; set; }
        public List<GraphRecipient>? ReplyTo { get; set; }
        public List<GraphRecipient>? BccRecipients { get; set; }
    }

    private class GraphItemBody
    {
        public required string ContentType { get; set; }
        public required string Content { get; set; }
    }

    private class GraphRecipient
    {
        public required GraphEmailAddress EmailAddress { get; set; }
    }

    private class GraphEmailAddress
    {
        public required string Address { get; set; }
        public string? Name { get; set; }
    }
}
