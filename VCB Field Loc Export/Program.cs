using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using static System.Net.Mime.MediaTypeNames;

// TODO:
// 1.  Upload the delta to the Google calendars
// 2.  Compute a delta (added entries, removed entries) to upload to Google for future updates.

namespace VcbFieldExport
{
    internal partial class Program
    {
        [GeneratedRegex(@"(\d+:\d+ (AM|PM))( - (\d+:\d+ (AM|PM)))?")]
        private static partial Regex TimeRegex();

        [GeneratedRegex(@"(?<TeamName>.+): (?<EventTitle>.+)", RegexOptions.ExplicitCapture)]
        private static partial Regex TeamEventRegex();

        [GeneratedRegex(@"(?<VisitorTeamName>.+) at (?<HomeTeamName>.+)", RegexOptions.ExplicitCapture)]
        private static partial Regex LeagueControlledGameRegex();

        [GeneratedRegex(@"(?<VcbTeamName>.+) vs\. (?<OpponentName>.+) \(Team Controlled\)?", RegexOptions.ExplicitCapture)]
        private static partial Regex TeamControlledGameRegex();

        [GeneratedRegex(@"<div class=""pagination .*<a.*\d+"">(\d+)</a>.*</a></div>")]
        private static partial Regex PaginationRegex();

        static readonly Dictionary<int, string> locationIds = new Dictionary<int, string> {
            { 69829182, "Killarney Park W diamond" },
        };

        static int Main(string[] args)
        {
            string sessionId = args[0];

            if (string.IsNullOrEmpty(sessionId))
            {
                Console.WriteLine("Usage VcbFieldExport.exe <TeamSnap session Id>");
                return -1;
            }

            string eventPattern = @"<tr.+>\s" +
            @"<td (colspan=""2"" )?class=""l-Schedule__col-game-event"">\s(<a .+>)?(?<EventName>[^<]+)(</a>)?\s</td>\s" +
            @"(<td class=""l-Schedule__col-result"">\s(.+\s){3}</td>\s)?<td class=""l-Schedule__col-date"">\s(?<Date>.+)\s</td>\s" +
            @"<td class=""l-Schedule__col-time"">\s(?<Time>(<span class=""red"">CANCELED</span>)|(\d+:\d+ (AM|PM) - \d+:\d+ (AM|PM))|(\d+:\d+ (AM|PM)))\s((.+)\s)?((.+)\s)?</td>\s";

            Regex eventRegex = new(eventPattern, RegexOptions.ExplicitCapture | RegexOptions.NonBacktracking | RegexOptions.Compiled);

            using HttpClient client = new();
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Add("Cookie", $"_ts_session={sessionId}");

            foreach (int locationId in locationIds.Keys) {
                Console.WriteLine($"Processing events for location {locationIds[locationId]}");
                int currentPage = 1;

                string responseHtml = GetVcbFieldEvents(client, locationId, currentPage);

                int contentStartIndex = responseHtml.IndexOf("<div id=\"content\">");

                if (contentStartIndex == -1) {
                    Console.WriteLine("Error - did not find the expected event table start tag - check the returned HTML content and update the script");
                    return -1;
                }

                Match m = PaginationRegex().Match(responseHtml, contentStartIndex);

                int expectedPageCount = m.Success ? Int32.Parse(m.Groups[1].Value) : 1;

                Console.WriteLine($"Expecting {expectedPageCount} pages of events for this location.");

                StreamWriter outputFile = new($"{locationIds[locationId]}.csv");
                outputFile.WriteLine("Event/Game,Home Team,Event title or Visiting Team,Start Time,End Time");

                while (currentPage <= expectedPageCount) {
                    Console.WriteLine($"Processing page {currentPage} ...");

                    int eventCount = 0;
                    Match eventMatches = eventRegex.Match(responseHtml, contentStartIndex);

                    while (eventMatches.Success) {
                        DateTime startTime, endTime;

                        Match timeMatch = TimeRegex().Match(eventMatches.Groups["Time"].Value);

                        if (timeMatch.Success) {
                            startTime = DateTime.Parse(eventMatches.Groups["Date"].Value) + DateTime.Parse(timeMatch.Groups[1].Value).TimeOfDay;

                            if (!string.IsNullOrEmpty(timeMatch.Groups[4].Value)) {
                                endTime = DateTime.Parse(eventMatches.Groups["Date"].Value) + DateTime.Parse(timeMatch.Groups[4].Value).TimeOfDay;
                            }
                            else {
                                endTime = startTime.AddHours(3);
                            }

                            string gameOrEventString = eventMatches.Groups["EventName"].Value.Trim();

                            // Event: VCB 13U A Giants: Practice,  // Events will have a colon that separates
                            // Game (league controlled): VCB 13U A Red Sox at VCB 13U A Diamondbacks  // Games will have a <visiting team> "at" <VCB team>
                            // Game (team-controlled): VCB Expos 15U AA - Blue vs.Vic - Layritz(Team Controlled)

                            Match teamEvent = TeamEventRegex().Match(gameOrEventString);

                            if (teamEvent.Success) {
                                outputFile.WriteLine($"Team event,{teamEvent.Groups["TeamName"].Value},{teamEvent.Groups["EventTitle"].Value},{startTime},{endTime}");
                            }
                            else {
                                Match leagueControlledGame = LeagueControlledGameRegex().Match(gameOrEventString);

                                if (leagueControlledGame.Success) {
                                    outputFile.WriteLine($"Game,{leagueControlledGame.Groups["HomeTeamName"].Value},{leagueControlledGame.Groups["VisitorTeamName"].Value},{startTime},{endTime}");
                                }
                                else {
                                    Match teamControlledGame = TeamControlledGameRegex().Match(gameOrEventString);
                                    if (teamControlledGame.Success) {
                                        outputFile.WriteLine($"Game,{teamControlledGame.Groups["VcbTeamName"].Value},{teamControlledGame.Groups["OpponentName"].Value},{startTime},{endTime}");
                                    }
                                    else {
                                        Console.WriteLine($"Warning: unable to parse the event string for the event {gameOrEventString}.  Check the returned HTML");
                                        outputFile.WriteLine($"Event,{gameOrEventString},,{startTime},{endTime}");
                                    }
                                }
                            }

                            eventCount++;
                        }
                        else {
                            Console.WriteLine($"Warning: Event {eventMatches.Groups["EventName"].Value.Trim()} on {eventMatches.Groups["Date"].Value} has an unparsable time value ({eventMatches.Groups["Time"].Value}).  Skipping...");
                        }

                        eventMatches = eventMatches.NextMatch();
                    }

                    Console.WriteLine($"Parsed {eventCount} events from this page.");

                    currentPage++;

                    responseHtml = GetVcbFieldEvents(client, locationId, currentPage);
                    contentStartIndex = responseHtml.IndexOf("<div id=\"content\">");

                    if (contentStartIndex == -1)
                    {
                        Console.WriteLine("Error - did not find the expected event table start tag - check the returned HTML content and update the script");
                        return -1;
                    }
                }

                outputFile.Close();
            }

            return 0;
        }

        static string GetVcbFieldEvents(HttpClient client, int locationId, int page)
        {
            return client.GetStringAsync($"https://go.teamsnap.com/774786/league_schedule?location_id={locationId}&page={page}").Result;
        }
    }
}
