using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

//namespace Shared
//{
    public class Credentials
    {
        [JsonProperty("teamsnap_bearer_token")]
        // TODO: Figure out how to get a bearer token from TeamSnap's service using the client ID & secret.
        // Fortunately, the bearer token seems to be perpertual so we can just put that into our credential file
        // until the above TODO is done.
        public string? Teamsnap { get; set; }
        public Google.Apis.Auth.OAuth2.ClientSecrets? Sportsengine { get; set; }
        public Google.Apis.Auth.OAuth2.ClientSecrets? Assignr { get; set; }
        public Google.Apis.Auth.OAuth2.ClientSecrets? Google { get; set; }
    }
//}
