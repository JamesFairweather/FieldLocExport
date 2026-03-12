using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.ComponentModel;
using System.Text.RegularExpressions;

namespace VcbFieldExport
{
    public class Datum
    {
        public Datum()
        {
            name = string.Empty;
            value = string.Empty;
        }

        public string name { get; set; }
        public string value { get; set; }
    }

    public class Item
    {
        Item()
        {
            data = new();
        }

        public List<Datum> data { get; set; }
    }

    public class Collection
    {
        public Collection()
        {
            version = string.Empty;
            items = new();
        }
        public string version { get; set; }
        public List<Item> items { get; set; }
    }

    public class TeamSnapResponseRoot
    {
        public TeamSnapResponseRoot()
        {
            collection = new();
        }

        public Collection collection { get; set; }
    }


    class TeamSnapEvents
    {
        Dictionary<string, string> TeamIdToDivision = new Dictionary<string, string>
        {
            { "10483142", "18U AAA" },  // VCB 18U AAA White Mounties
            { "10483271", "18U AAA" },  // VCB 18U AAA Blue Expos
            { "10483153", "15U AAA" },  // VCB 15U AAA
            { "10501427", "15U AA" },   // VCB 15U AA Red
            { "10453428", "15U AA" },   // VCB 15U AA Expos Blue
            { "10480810", "13U AAA" },  // VCB 13U AAA
            { "10453427", "13U AA" },   // VCB 13U AA
            { "10538952", "18U AA" },   // Yankees
            { "10538951", "18U AA" },   // Reds
            { "10538950", "18U AA" },   // Rangers
            { "10538949", "18U AA" },   // Pirates
            { "10538948", "18U AA" },   // Phillies
            { "10538947", "18U AA" },   // Marlins
            { "10538946", "18U AA" },   // Mariners
            { "10538945", "18U AA" },   // Dodgers
            { "10538944", "18U AA" },   // Blue Jays
            { "10538943", "18U AA" },   // Athletics
            { "10547272", "26U" },      // Expos
            { "10547273", "26U" },      // Mounties
            { "10547274", "26U" },      // Vipers
        };

        public TeamSnapEvents(StreamWriter logger)
        {
            // How to obtain a TeamSnap Bearer token for accessing the service
            //
            // Documentation links
            // https://www.teamsnap.com/documentation/apiv3/getting-started
            // https://www.teamsnap.com/documentation/apiv3/authorization
            //
            // This program uses the Token Authentication Flow as it's not a web-based application
            // But I couldn't get the https://auth.teamsnap.com/oauth/authorize endpoint to work from
            // code here.  However, I was able to get a perpertual Bearer token by following these steps:
            //
            // 1. Open https://auth.teamsnap.com/ in the web browser, and sign in if necessary
            // 2. Click on your name, then Your Applications, then Assignr Consistency Checker.
            // 3. Right-click on the "Authorize" button, copy the URL, but change the response_type URL
            //    parameter to "token" when it's pasted back into the address bar
            // 4. in the redirect URL, the server sends back an access token in the address bar, which is
            //    the Bearer token
            //
            // The above steps can be implemented in code but I couldn't figure out how to read back
            // the token from the redirect URI.  I'm sure that's possible, I just don't know how.
            // The Web Application flow also uses a redirect URI, which has the same problem as above:
            // I don't know how to get that back into this program.

            mLogger = logger;
        }

        public void FetchEvents(string bearerToken, List<Field> fieldInfo)
        {
            List<VcbFieldEvent> nonLeagueUnmatchedGamesBetweenVcbTeams = new();

            using HttpClient client = new();
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {bearerToken}");

            foreach (Field field in fieldInfo)
            {
                if (!field.Valid()) {
                    continue;
                }

                string jsonResponse = client.GetStringAsync($"https://api.teamsnap.com/v3/events/search?location_id={field.teamSnapId}&started_after={DateTime.Today:O}").Result;

                TeamSnapResponseRoot jsonRoot = JsonConvert.DeserializeObject<TeamSnapResponseRoot>(jsonResponse) ?? new();

                foreach (Item e in jsonRoot.collection.items)
                {
                    if (e.data.Find(x => x.name == "is_canceled")?.value == "true")
                    {
                        continue;   // skip canceled events
                    }

                    string location = e.data.Find(x => x.name == "location_name")?.value ?? "TBD";
                    DateTime startTime = DateTime.Parse(e.data.Find(x => x.name == "start_date")?.value ?? string.Empty).ToUniversalTime();
                    startTime = startTime.AddSeconds(-startTime.Second);    // sometimes TeamSnap events have non-zero seconds values, not sure why
                    string? endDateString = e.data.Find(x => x.name == "end_date")?.value;
                    DateTime endTime = (endDateString != null) ? DateTime.Parse(endDateString).ToUniversalTime() : startTime.AddHours(2);
                    endTime = endTime.AddSeconds(-endTime.Second);
                    string formatted_title = e.data.Find(x => x.name == "formatted_title")?.value ?? string.Empty;
                    string formatted_title_for_multi_team = e.data.Find(x => x.name == "formatted_title_for_multi_team")?.value ?? string.Empty;
                    string opponent_name = e.data.Find(x => x.name == "opponent_name")?.value ?? string.Empty;
                    bool leagueControlledGame = e.data.Find(x => x.name == "is_league_controlled")?.value == "true";
                    string teamId = e.data.Find(x => x.name == "team_id")?.value ?? string.Empty;

                    int index = formatted_title_for_multi_team.IndexOf(formatted_title);
                    if (index == -1)
                    {
                        throw new Exception("Did not find the event description in the formatted_title_for_multi_team value.  Check the TeamSnap API response");
                    }
                    string thisTeam = formatted_title_for_multi_team.Remove(index, formatted_title.Length).Trim();
                    string homeTeam = string.Empty;
                    string visitingTeam = string.Empty;

                    if (e.data.Find(x => x.name == "is_game")?.value == "true")
                    {
                        bool addGameToEventList;

                        if (e.data.Find(x => x.name == "game_type")?.value == "Home")
                        {
                            // always add home games.
                            homeTeam = thisTeam;
                            visitingTeam = opponent_name;
                            addGameToEventList = true;
                        }
                        else
                        {
                            // this team is the visiting team.  It will only be added in rare cases
                            homeTeam = opponent_name;
                            visitingTeam = thisTeam;

                            // If the game is controlled by the league, we want to skip adding beause it will be
                            // added by the home team.

                            // OR, if this is the visiting team's event for a game where the home team is also a VCB team,
                            // ignore it because the game will be added by the home team's entry.  We only want to
                            // add this entry if this is a team-controlled game where the home team is NOT a VCB team.
                            // There are a few games being played on VCB fields where the VCB team is the visiting
                            // team.  Assignr has these games of course, and we want TeamSnap to show them too.
                            // Except we still want to add the game if the home team is "VCB 15U TBD" because these are
                            // Girls team games where their opponent is still to be decided.
                            addGameToEventList = !((leagueControlledGame || (homeTeam.StartsWith("VCB") && homeTeam != "VCB 15U TBD")));
                        }

                        string division;

                        try {
                            division = TeamIdToDivision[teamId];
                        }
                        catch (KeyNotFoundException) {
                            throw new Exception($"Team {thisTeam} does not have a mapping to a division.  Please update the map.");
                        }

                        VcbFieldEvent.Type gameType = VcbFieldEvent.Type.Game;

                        // For 13UA, 15UA, and 18UAA:
                        // Games scheduled after the end of the regular season and before the end of the playoffs are
                        // playoff games.  Games after the end of the playoffs are summer ball games, but for the
                        // purposes of our game calendar, we'll treat them as regular-season games.
                        // These dates should be in a separate configuration file, not buried in code...  I'll fix that
                        // for 2026.
                        DateTime RegularSeasonEnd_13UA = new(2025, 5, 28);
                        DateTime PlayoffsEnd_13UA = new(2025, 6, 15);
                        DateTime RegularSeasonEnd_15UA = new(2025, 6, 12);
                        DateTime PlayoffsEnd_15UA = new(2025, 6, 23);
                        DateTime RegularSeasonEnd_18UAA = new(2025, 6, 16);
                        DateTime PlayoffsEnd_18UAA = new(2025, 6, 27);

                        if ((division == "15U A" && startTime > RegularSeasonEnd_13UA && startTime <= PlayoffsEnd_13UA) ||
                            (division == "13U A" && startTime > RegularSeasonEnd_15UA && startTime <= PlayoffsEnd_15UA) ||
                            (division == "18U AA" && startTime > RegularSeasonEnd_18UAA && startTime <= PlayoffsEnd_18UAA)) {
                            gameType = VcbFieldEvent.Type.PlayoffGame;
                        }

                        if (!leagueControlledGame
                            && homeTeam.StartsWith("VCB")
                            && visitingTeam.StartsWith("VCB")
                            && !visitingTeam.Contains("TBD")) {

                            // Check that a game between two VCB teams is in _both_ teams' TeamSnap schedules
                            VcbFieldEvent? oppositeGameFound = nonLeagueUnmatchedGamesBetweenVcbTeams.Find(e => e.location == location && e.startTime == startTime && e.homeTeam == homeTeam && e.visitingTeam == visitingTeam);

                            if (oppositeGameFound == null) {
                                // First instance of this game.  We should find another one in their opponent's TeamSnap schedule later
                                nonLeagueUnmatchedGamesBetweenVcbTeams.Add(new(VcbFieldEvent.Type.Game, location, startTime, division, homeTeam, visitingTeam, string.Empty, true));
                            }
                            else {
                                // This game was added from the other team's schedule, so we can remove it from the unmatched list
                                nonLeagueUnmatchedGamesBetweenVcbTeams.Remove(oppositeGameFound);
                            }
                        }

                        if (addGameToEventList) {
                            mGames.Add(new VcbFieldEvent(gameType, location, startTime, division, homeTeam, visitingTeam, string.Empty, true));
                        }
                    }
                    else {
                        mPractices.Add(new VcbFieldEvent(location, startTime, endTime, thisTeam, formatted_title));
                    }
                }
            }

            if (nonLeagueUnmatchedGamesBetweenVcbTeams.Count != 0) {
                mLogger.WriteLine("Some games are missing from a VCB opponent's TeamSnap schedule");

                foreach(VcbFieldEvent e in nonLeagueUnmatchedGamesBetweenVcbTeams) {
                    mLogger.WriteLine($"Location {e.location} and Date: {e.startTime.ToLocalTime().ToString("g")}.  Home team: {e.homeTeam}.  Visiting team: {e.visitingTeam}");
                }
            }
        }
        public void addPlayoffPlaceHolderGames(List<VcbFieldEvent> placeHolderGames) {
            mGames.AddRange(placeHolderGames);
        }

        public int FindConflicts()
        {
            int conflicts = 0;

            // TODO

            return conflicts;
        }

        public List<VcbFieldEvent> getPractices()
        {
            return mPractices;
        }
        public List<VcbFieldEvent> getGames()
        {
            return mGames;
        }

        List<VcbFieldEvent> mPractices = new();
        List<VcbFieldEvent> mGames = new();

        StreamWriter mLogger;
    }
}
