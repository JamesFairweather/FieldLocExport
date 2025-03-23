using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VcbFieldExport;
using Google.Apis.Auth.OAuth2;

namespace VcbFieldExport
{
    class TeamSnapEvents
    {
        public TeamSnapEvents()
        {
            // How to obtain a TeamSnap Bearer token for accessing the service
            //
            // Documentation
            // https://www.teamsnap.com/documentation/apiv3/getting-started
            // https://www.teamsnap.com/documentation/apiv3/authorization
            //
            // This program uses the Token Authentication Flow as it's not a web-based application
            // I couldn't get the https://auth.teamsnap.com/oauth/authorize endpoint to work from code here.
            // But I was able to get a Bearer token this way:
            // * Open https://auth.teamsnap.com/ in the web browser (and sign in if necessary)
            // * Click on your name, then Your Applications, then Assingr Consistency Checker.
            // * Right-click on the "Authorize" button, copy the URL, but change the response_type URL
            // parameter to token when it's pasted back into the address bar
            // * in the redirect URL, the server sends back an access token in the address bar, which is the
            // Bearer token
            mBearerToken = File.ReadAllText("TeamsnapBearerToken.txt");
        }

        public void FetchEvents()
        {
            List<string> TEAMSNAP_LOCATION_IDS = new List<string> {
                "74226229", // Chaldecott Park N diamond
                "74226228", // Chaldecott Park S diamond
                "74226227", // Hillcrest Park NE diamond
                "74226226", // Hillcrest Park SW diamond
                "74226230", // Nanaimo Park N diamond
                "74226231", // Nanaimo Park SE diamond
                "74226232", // Trafalgar Park
                "74242257", // Nanaimo Park batting cage
            };

            using HttpClient client = new();
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {mBearerToken}");
            string dateString = DateTime.Now.ToString("yyyy-MM-dd");

            foreach (string teamSnapLocationId in TEAMSNAP_LOCATION_IDS)
            {
                string jsonResponse = client.GetStringAsync($"https://api.teamsnap.com/v3/events/search?location_id={teamSnapLocationId}&started_after={dateString}").Result;

                // string jsonResponse = File.ReadAllText("teamSnapApiResponse.json");

                JObject? jsonRoot = JsonConvert.DeserializeObject(jsonResponse) as JObject;

                JArray eventList = jsonRoot["collection"]["items"] as JArray;

                Dictionary<JValue, string> teamSnapEventFields = new Dictionary<JValue, string> {
                    { (JValue)"location_name", "" },
                    { (JValue)"start_date", "" },
                    { (JValue)"end_date", "" },
                    { (JValue)"is_game", "" },                 // boolean
                    { (JValue)"is_league_controlled", "" },    // boolean
                    { (JValue)"is_canceled", "" },             // boolean
                    { (JValue)"game_type", "" },               // Home/Away
                    { (JValue)"formatted_title", "" },
                    { (JValue)"formatted_title_for_multi_team", "" },
                    { (JValue)"opponent_name", "" },
                };

                foreach (JObject e in eventList)
                {
                    JArray eventInfo = e["data"] as JArray;

                    // store all the useful properties in a dictionary
                    foreach (JObject prop in eventInfo)
                    {
                        JValue propName = (JValue)prop["name"];
                        if (teamSnapEventFields.ContainsKey(propName))
                        {
                            teamSnapEventFields[propName] = prop["value"].ToString();
                        }
                    }

                    if (teamSnapEventFields[(JValue)"is_canceled"] == "True")
                    {
                        continue;   // skip canceled events
                    }

                    VcbFieldEvent.Type eventType = teamSnapEventFields[(JValue)"is_game"] == "True" ? VcbFieldEvent.Type.Game : VcbFieldEvent.Type.Practice;
                    string location = teamSnapEventFields[(JValue)"location_name"];
                    DateTime startTime = DateTime.Parse(teamSnapEventFields[(JValue)"start_date"]).ToLocalTime();
                    startTime = startTime.AddSeconds(-startTime.Second);        // some 2025 13U AA home games have non-zero seconds values in their start times for some reason
                    string endDateString = teamSnapEventFields[(JValue)"end_date"];
                    DateTime endTime = string.IsNullOrEmpty(endDateString) ? startTime.AddHours(2) : DateTime.Parse(endDateString).ToLocalTime();
                    string formatted_title = teamSnapEventFields[(JValue)"formatted_title"];
                    string formatted_title_for_multi_team = teamSnapEventFields[(JValue)"formatted_title_for_multi_team"];
                    string opponent_name = teamSnapEventFields[(JValue)"opponent_name"];
                    bool homeGame = teamSnapEventFields[(JValue)"game_type"] == "Home";
                    bool leageControlledGame = teamSnapEventFields[(JValue)"is_league_controlled"] == "True";

                    int index = formatted_title_for_multi_team.IndexOf(formatted_title);
                    if (index == -1)
                    {
                        throw new Exception("Did not find the event description in the formatted_title_for_multi_team value.  Check the TeamSnap API response");
                    }
                    string thisTeam = formatted_title_for_multi_team.Remove(index, formatted_title.Length).Trim();
                    string homeTeam = string.Empty;
                    string visitingTeam = string.Empty;
                    string eventDescription = string.Empty;

                    if (eventType == VcbFieldEvent.Type.Practice)
                    {
                        eventDescription = formatted_title;
                        homeTeam = thisTeam;
                    }
                    else
                    {
                        if (homeGame)
                        {
                            // always add home games.
                            homeTeam = thisTeam;
                            visitingTeam = opponent_name;
                        }
                        else
                        {
                            // this team is the visiting team.  It will only be added in rare cases
                            homeTeam = opponent_name;
                            visitingTeam = thisTeam;

                            if (leageControlledGame || homeTeam.StartsWith("VCB"))
                            {
                                // If the game is controlled by the league, we want to skip adding beause it will be
                                // added by the home team

                                // OR, if this is the visiting team's event for a game where the home team is also a VCB team,
                                // ignore it because the game will be added by the home team's entry.  We only want to
                                // add this entry if this is a team-controlled game where the home team is NOT a VCB team.
                                // There are a few games being played on VCB fields where the VCB team is the visiting
                                // team.  Assignr has these games of course, and we want TeamSnap to show them too.
                                continue;
                            }
                        }
                    }

                    mEvents.Add(new VcbFieldEvent(eventType, location, startTime, homeTeam, visitingTeam, endTime, eventDescription));
                }
            }
        }

        public int FindConflicts()
        {
            int conflicts = 0;

            // TODO

            return conflicts;
        }

        public List<VcbFieldEvent> getEventList() {
            return mEvents;
        }

        string mBearerToken;

        List<VcbFieldEvent> mEvents = new();
    }
}
