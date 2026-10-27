using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Core;
using Azure.Identity;

namespace M365AcademyCrm.Services;

/// <summary>
/// Checks CRM group membership via Microsoft Graph app-only auth, using the
/// CRM app registration's own credentials (AzureAd:TenantId / ClientId /
/// ClientSecret). Needs the GroupMember.Read.All application permission with
/// admin consent - see README.md.
/// </summary>
public class GraphGroupChecker : IGraphGroupChecker
{
    private static readonly string[] GraphScope = ["https://graph.microsoft.com/.default"];

    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private readonly ILogger<GraphGroupChecker> _logger;

    public GraphGroupChecker(IConfiguration configuration, HttpClient httpClient, ILogger<GraphGroupChecker> logger)
    {
        _configuration = configuration;
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IReadOnlySet<string>> GetMemberGroupsAsync(string userObjectId, IEnumerable<string> groupIds, CancellationToken cancellationToken)
    {
        var wanted = groupIds.Where(g => !string.IsNullOrWhiteSpace(g) && g != "CHANGE_ME").Distinct().ToList();
        if (wanted.Count == 0)
        {
            _logger.LogWarning("No CRM group IDs are configured (Crm:UserGroupId / Crm:AdminGroupId) - nobody can be granted CRM access.");
            return new HashSet<string>();
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));
            var timeoutToken = timeoutCts.Token;

            var credential = new ClientSecretCredential(
                _configuration["AzureAd:TenantId"] ?? string.Empty,
                _configuration["AzureAd:ClientId"] ?? string.Empty,
                _configuration["AzureAd:ClientSecret"] ?? string.Empty);
            var token = await credential.GetTokenAsync(new TokenRequestContext(GraphScope), timeoutToken);

            var payload = new CheckMemberGroupsRequest { GroupIds = wanted };
            using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(userObjectId)}/checkMemberGroups")
            {
                Content = content,
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

            var response = await _httpClient.SendAsync(request, timeoutToken);
            var body = await response.Content.ReadAsStringAsync(timeoutToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Graph checkMemberGroups failed with {StatusCode}: {Body}", response.StatusCode, body);
                return new HashSet<string>();
            }

            var result = JsonSerializer.Deserialize<CheckMemberGroupsResponse>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return new HashSet<string>(result?.Value ?? [], StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check CRM group membership for user {UserObjectId}.", userObjectId);
            return new HashSet<string>();
        }
    }

    private class CheckMemberGroupsRequest
    {
        [JsonPropertyName("groupIds")]
        public required List<string> GroupIds { get; set; }
    }

    private class CheckMemberGroupsResponse
    {
        public List<string>? Value { get; set; }
    }
}
