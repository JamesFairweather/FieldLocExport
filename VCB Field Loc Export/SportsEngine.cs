using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VcbFieldExport;

namespace VCBFieldExport
{
    internal class OAuthCredentials
    {
        public OAuthCredentials()
        {
            Id = string.Empty;
            Secret = string.Empty;
        }

        [JsonProperty("client_id")]
        public string Id { get; set; }

        [JsonProperty("client_secret")]
        public string Secret { get; set; }
    }

    internal class SportsEngine
    {
        public SportsEngine() {
            mHttpClient = new();
        }

        public void authenticate() {
            OAuthCredentials? credentials;
            using (StreamReader reader = new StreamReader("sportsengine.json"))
            {
                string json = reader.ReadToEnd();
                credentials = JsonConvert.DeserializeObject<OAuthCredentials>(json);

                if (credentials == null)
                {
                    throw new Exception("Unable to deserialize the SportsEngine credentials");
                }
            }

            string authUrl = $"https://user.sportsengine.com/oauth/token";

            mHttpClient.DefaultRequestHeaders.Clear();
            mHttpClient.DefaultRequestHeaders.Add("cache-control", "no-cache");

            var oauthHeaders = new Dictionary<string, string> {
                {"client_id", credentials.Id},
                {"client_secret", credentials.Secret},
                {"grant_type", "client_credentials"},
              };

            string oauthUri = "https://user.sportsengine.com/oauth/token";

            // Get a Bearer token for further API requests
            HttpResponseMessage tokenResponse = mHttpClient.PostAsync(oauthUri, new FormUrlEncodedContent(oauthHeaders)).Result;
            string jsonContent = tokenResponse.Content.ReadAsStringAsync().Result;

            if (string.IsNullOrEmpty(jsonContent)) {
                throw new Exception($"Unexpected response from the Assignr service: {jsonContent}");
            }

            Token? tok = JsonConvert.DeserializeObject<Token>(jsonContent);

            if (tok == null) {
                throw new Exception($"Unexpected response from the Assignr service: {jsonContent}");
            }

            if (tok.TokenType != "bearer") {
                throw new Exception($"We expected the returned token to be a bearer token.  But it is {tok.TokenType} instead.");
            }

            mBearerToken = tok.AccessToken;
        }

        public void fetchEvents()
        {
//            string body = @"
//{
//	""query"": ""query organizations {\n    organizations(page: 1, perPage: 50) {\n        pageInformation {\n            pages\n            count\n            page\nperPage\n        }\n        results {\n            id\n            name\n        }\n    }\n}"",
//	""operationName"": ""organizations""
//}";
            //query organizations {
            //    organizations(page: 1, perPage: 50) {
            //        pageInformation {
            //            pages
            //            count
            //            page
            //perPage
            //        }
            //        results {
            //            id
            //            name
            //        }
            //    }
            //}";

            string LMB_ORGANIZATION_ID = "144187";  // from the query above

            // GAME can also be EVENT if we want to put these events on a public field calendar.  But for Assignr reconciliation,
            // I only need GAME.

            string pageInfo = @"pageInformation { pages count page perPage }";
            string results = @"results { name type start end location { name } eventTeams { team { name } homeTeam } } }";

            int pageNumber = 1;
            string query = $"query events {{ events( organizationId: {LMB_ORGANIZATION_ID} from: \\\"{DateTime.Now.ToString("yyyy-MM-dd")}\\\" perPage: 10 page: {pageNumber} calendarEventType: GAME) {{ {pageInfo} {results} }}";

            string uri = "https://api.sportsengine.com/graphql";
            HttpRequestMessage msg = new HttpRequestMessage(HttpMethod.Post, uri);
            msg.Headers.Add("authorization", $"Bearer {mBearerToken}");
            msg.Content = new StringContent($"{{ \"query\": \"{query}\", \"operationName\": \"events\" }}", Encoding.UTF8, "application/json");

            HttpResponseMessage gamesResponse = mHttpClient.Send(msg);

            if (gamesResponse.StatusCode != System.Net.HttpStatusCode.OK)
            {
                throw new Exception("Failed to retrieve game information from Assignr");
            }

            string result = gamesResponse.Content.ReadAsStringAsync().Result;
        }

        HttpClient mHttpClient;
        string? mBearerToken;
    }
}
