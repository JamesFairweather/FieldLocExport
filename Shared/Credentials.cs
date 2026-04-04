using Newtonsoft.Json;

public class Credentials
{
    public Credentials()
    {
        AssignrSessionToken = string.Empty;
    }

    [JsonProperty("teamsnap_bearer_token")]
    // TODO: Figure out how to get a bearer token from TeamSnap's service using the client ID & secret.
    // Fortunately, the bearer token seems to be perpertual so we can just put that into our credential file
    // until the above TODO is done.
    public string? Teamsnap { get; set; }

    [JsonProperty("assignr_session_token")]
    // Get this from a web browser after signing into Assignr.  IDK how long it's valid, but I really only
    // need it once a week.  Still a lot faster than pulling the game requests from a browser session into
    // a spreadsheet so I can analyze them.
    public string AssignrSessionToken { get; set; }
    public Google.Apis.Auth.OAuth2.ClientSecrets? Sportsengine { get; set; }
    public Google.Apis.Auth.OAuth2.ClientSecrets? Assignr { get; set; }
    public Google.Apis.Auth.OAuth2.ClientSecrets? Google { get; set; }
}
