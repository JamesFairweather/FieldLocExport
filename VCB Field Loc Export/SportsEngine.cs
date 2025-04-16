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

            string safeRedirect = System.Web.HttpUtility.UrlEncode("http://localhost");

            string authUrl = $"https://user.sportsengine.com/oauth/authorize?client_id=62abbeb26f91815ffcc75767b24ef699&redirect_uri={safeRedirect}&response_type=code";

            mHttpClient.DefaultRequestHeaders.Clear();
            mHttpClient.DefaultRequestHeaders.Add("cache-control", "no-cache");

            HttpResponseMessage response = mHttpClient.GetAsync(authUrl).Result;

            var oauthHeaders = new Dictionary<string, string> {
                {"client_id", credentials.Id},
                {"client_secret", credentials.Secret},
                {"code", "8f85cc86d3f626ca0852fec16759dc23" },
                {"grant_type", "authorization_code"},
              };

            string oauthUri = "https://user.sportsengine.com/oauth/token";

            // Get a Bearer token for further API requests
            HttpResponseMessage tokenResponse = mHttpClient.PostAsync(oauthUri, new FormUrlEncodedContent(oauthHeaders)).Result;
            string jsonContent = tokenResponse.Content.ReadAsStringAsync().Result;
        }

        public void fetchEvents()
        {
            string body = @"
{
	""query"": ""query organizations {\n    organizations(page: 1, perPage: 50) {\n        pageInformation {\n            pages\n            count\n            page\nperPage\n        }\n        results {\n            id\n            name\n        }\n    }\n}"",
	""operationName"": ""organizations""
}";
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

            string uri = "https://api.sportsengine.com/graphql";
            HttpRequestMessage msg = new HttpRequestMessage(HttpMethod.Post, uri);
            msg.Headers.Add("authorization", $"Bearer {mBearerToken}");
            //msg.Headers.Add("Content-Type", "application/json");
            msg.Content = new StringContent(body, Encoding.UTF8, "application/json");

            HttpResponseMessage gamesResponse = mHttpClient.Send(msg);

            if (gamesResponse.StatusCode != System.Net.HttpStatusCode.OK)
            {
                throw new Exception("Failed to retrieve game information from Assignr");
            }

            string result = gamesResponse.Content.ReadAsStringAsync().Result;
        }

        HttpClient mHttpClient;
        string? mBearerToken = "c8e0a6d3deb7130ba0cb156d67b845b3";
    }
}
