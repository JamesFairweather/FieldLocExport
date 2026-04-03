using Newtonsoft.Json;

public class Token
{
    Token()
    {
        AccessToken = string.Empty;
        TokenType = string.Empty;
        ExpiresIn = 0;
        Scope = string.Empty;
        Created = 0;
    }

    [JsonProperty("access_token")]
    public string AccessToken { get; set; }

    [JsonProperty("token_type")]
    public string TokenType { get; set; }

    [JsonProperty("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonProperty("scope")]
    public string Scope { get; set; }
    [JsonProperty("created_at")]
    public int Created { get; set; }
}
