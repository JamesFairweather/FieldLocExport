using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VcbFieldExport;
using static Google.Apis.Requests.BatchRequest;

namespace VcbFieldExport
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
            string LMB_ORGANIZATION_ID = "144187";  // from the query above

            string pageInfo = @"pageInformation { pages count page perPage }";
            string results = @"results { name type start end location { name } eventTeams { team { name } homeTeam } } }";

            int pageNumber = 1;
            int pageSize = 100;
            int eventCount = 0;

            while (true) {

                // If we want to put these events on a public calendar, we'll need GAMEs and EVENTs for the calendarEventType
                string query = $"query events {{ events( organizationId: {LMB_ORGANIZATION_ID} from: \\\"{DateTime.Now.ToString("yyyy-MM-dd")}\\\" perPage: {pageSize} page: {pageNumber} calendarEventType: GAME) {{ {pageInfo} {results} }}";

                string uri = "https://api.sportsengine.com/graphql";
                HttpRequestMessage msg = new HttpRequestMessage(HttpMethod.Post, uri);
                msg.Headers.Add("authorization", $"Bearer {mBearerToken}");
                msg.Content = new StringContent($"{{ \"query\": \"{query}\", \"operationName\": \"events\" }}", Encoding.UTF8, "application/json");

                HttpResponseMessage gamesResponse = mHttpClient.Send(msg);

                if (gamesResponse.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    throw new Exception("Failed to retrieve game information from Assignr");
                }
                string jsonResponse = gamesResponse.Content.ReadAsStringAsync().Result;

                // The default behavior of the JSON deserializer is to convert datetime strings, but it appears to have a
                // bug because it's ignoring the "Z" suffix, which means the timestamp is in UTC, not local
                JsonSerializerSettings settings = new();
                settings.DateParseHandling = DateParseHandling.None;
                JObject jsonRoot = JsonConvert.DeserializeObject(jsonResponse, settings) as JObject ?? new JObject();
                JObject? pageInformation = jsonRoot["data"]["events"]["pageInformation"] as JObject;

                eventCount = (int)pageInformation["count"];
                if (pageNumber != (int)pageInformation["page"]) {
                    throw new Exception("Unexpected page returned");
                }

                JArray eventList = jsonRoot["data"]["events"]["results"] as JArray;

                foreach (JObject e in eventList) {
                    string location = (string)e["location"]["name"];
                    string strStartTime = (string)e["start"];
                    DateTime startTime = DateTime.Parse(strStartTime).ToUniversalTime();
                    string strEndTime = (string)e["start"];
                    DateTime endTime = DateTime.Parse(strEndTime).ToUniversalTime();
                    string homeTeam = (string)e["eventTeams"][0]["team"]["name"];
                    string visitingTeam = (string)e["eventTeams"][1]["team"]["name"];

                    mGames.Add(new VcbFieldEvent(VcbFieldEvent.Type.Game, location, startTime, homeTeam, visitingTeam, endTime, string.Empty));
                }

                if (mGames.Count == eventCount) {
                    break;  // while(true} loop exit
                }

                ++pageNumber;
            }
        }

        public List<VcbFieldEvent> getGames() {
            return mGames;
        }

        List<VcbFieldEvent> mGames = new();

        HttpClient mHttpClient;
        string? mBearerToken;
    }
}
