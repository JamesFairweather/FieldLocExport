
using Newtonsoft.Json;

namespace AutoAssign
{
    // To get game requests, we can use an undocumented API call:
    // https://littlemountainbaseball.assignr.com/assign/games/{gameId}/edit
    // This returns a small form in HTML format, but it's easily parsed.  Goto form->table->Pending Requests->find the tag data-game-id="{gameId}" data-assignment-id="{out_requestId_Plate}" e.g. 77199786
    // Then, we can do a get on this UTRL: https://littlemountainbaseball.assignr.com/assign/assignments/77199786.json
    // returns a JSON object with all users, each one indicating whether they've submitted a request for that game.  All the people who requested the game are listed at the top.
    // There isn't a public API to assign an official, but there is one to unassign all officials: /v2/games/{id}/unassign
    // To assign an official: 
    // PUT https://littlemountainbaseball.assignr.com/assign/games/{gameId}
    // Have to pass a User ID (mine is 340011)
    // As well as the assignment Id (e.g. 77267314)
    // It's not clear to me how these parameters are being passed back to the service from the stream I can see in Chrome

    internal class Token
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

    public class Assignr
    {
        public Assignr()
        {
            mHttpClient = new();
        }

        public void Authenticate(Google.Apis.Auth.OAuth2.ClientSecrets? credentials)
        {
            if (credentials == null)
            {
                throw new ArgumentException("credentials cannot be null");
            }

            mHttpClient.DefaultRequestHeaders.Clear();
            mHttpClient.DefaultRequestHeaders.Add("cache-control", "no-cache");

            var oauthHeaders = new Dictionary<string, string> {
                {"client_id", credentials.ClientId ?? string.Empty},
                {"client_secret", credentials.ClientSecret ?? string.Empty},
                {"scope", "read write" },
                {"grant_type", "client_credentials"},
              };

            string oauthUri = @"https://app.assignr.com/oauth/token";

            // Get a Bearer token for further API requests
            HttpResponseMessage tokenResponse = mHttpClient.PostAsync(oauthUri, new FormUrlEncodedContent(oauthHeaders)).Result;
            string jsonContent = tokenResponse.Content.ReadAsStringAsync().Result;
            if (string.IsNullOrEmpty(jsonContent))
            {
                throw new Exception($"Unexpected response from the Assignr service: {jsonContent}");
            }

            Token? tok = JsonConvert.DeserializeObject<Token>(jsonContent);

            if (tok == null)
            {
                throw new Exception($"Unexpected response from the Assignr service: {jsonContent}");
            }

            mBearerToken = tok.AccessToken;
        }

        HttpClient mHttpClient;
        string? mBearerToken;
    }

    internal partial class AutoAssign
    {
        static int Main(string[] args)
        {
            Console.WriteLine("Hello, World!");

            Assignr assignr = new();

            return 0;
        }
    }
}
