using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Calendar.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VcbFieldExport;
using CsvHelper;
using System.Globalization;
using CsvHelper.Configuration;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace VcbFieldExport
{
    // TODO:
    // * improve handling of failures from the Google service
    //   e.g. after a few days, the field account tokens expire and we get exceptions like this:
    // Google.Apis.Auth.OAuth2.Responses.TokenResponseException: Error: "invalid_grant", Description: "Token has been expired or revoked.", Uri: ""
    //    at Google.Apis.Auth.OAuth2.Responses.TokenResponse.FromHttpResponseAsync(HttpResponseMessage response, IClock clock, ILogger logger)
    // 
    // * there's probably some way to refresh the local tokens automatically.
    // refer to https://www.codeproject.com/Articles/64474/How-to-Read-the-Google-Calendar-in-Csharp for a potentially better way
    // to authenticate to the Google service
    internal partial class Program
    {
        [GeneratedRegex(@"(?<EventType>.+)\s:\s(?<homeTeam>.+)")]
        public static partial Regex SummaryRegex();

        [GeneratedRegex(@"vs\.\s(?<visitingTeam>.+)")]
        public static partial Regex DescriptionRegex();
    }

    class GoogleEvents
    {
        List<string> LOCATION_NAMES = new List<string> {
            "Chaldecott Park N diamond",
            "Chaldecott Park S diamond",
            "Hillcrest Park NE diamond",
            "Hillcrest Park SW diamond",
            "Nanaimo Park N diamond",
            "Nanaimo Park SE diamond",
            "Trafalgar Park",
        };

        List<VcbFieldEvent> mNewEventList;

        public GoogleEvents(List<VcbFieldEvent> games, List<VcbFieldEvent> practices)
        {
            mNewEventList = new List<VcbFieldEvent>(games.Count + practices.Count);
            mNewEventList.AddRange(games);
            mNewEventList.AddRange(practices);
        }


        CalendarService GetGoogleCalendarService(string locationName)
        {
            UserCredential credential;

            // If the scopes are modified, you will need a new credentials.json file and re-generate the tokens.

            string[] Scopes = { CalendarService.Scope.Calendar };
            string ApplicationName = "Google Calendar API .NET Quickstart";

            using (var stream =
              new FileStream("google_credentials.json", FileMode.Open, FileAccess.Read))
            {
                // The file token.json stores the user's access and refresh tokens, and is created
                // automatically when the authorization flow completes for the first time.
                string credPath = $"{locationName}.json";
                credential = GoogleWebAuthorizationBroker.AuthorizeAsync(
                  GoogleClientSecrets.FromStream(stream).Secrets,
                  Scopes,
                  "user",
                  CancellationToken.None,
                  new FileDataStore(credPath, true)).Result;
            }

            // Create Google Calendar API service.
            var service = new CalendarService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = ApplicationName,
            });

            return service;
        }

        void addEventToGoogleCalendar(CalendarService calendarService, VcbFieldEvent vcbFieldEvent) {
            Event googleCalendarEvent = new();

            googleCalendarEvent.Start = new EventDateTime()
            { DateTimeDateTimeOffset = vcbFieldEvent.startTime };

            googleCalendarEvent.End = new EventDateTime()
            { DateTimeDateTimeOffset = vcbFieldEvent.endTime };

            googleCalendarEvent.Summary = vcbFieldEvent.eventType.ToString() + " : " + vcbFieldEvent.homeTeam;
            googleCalendarEvent.Description = (vcbFieldEvent.eventType == VcbFieldEvent.Type.Practice ? vcbFieldEvent.description : "vs. " + vcbFieldEvent.visitingTeam);

            Event result = new ();

            try
            {
                result = calendarService.Events.Insert(googleCalendarEvent, "primary").Execute();
                // vcbFieldEvent.googleEventId = result.Id;
                Console.WriteLine($"Added event {vcbFieldEvent.eventType} at {vcbFieldEvent.location} on {vcbFieldEvent.startTime} for team {vcbFieldEvent.homeTeam}");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }

        // Uncomment these lines to remove all the events for a specific calendar
        // string removeAllEventsFromFieldCalendar = "Chaldecott Park N diamond";
        // DeleteAllCalendarEvents(GetGoogleCalendarService("Nanaimo Park N diamond"));
        // return 0;

        void DeleteAllCalendarEvents(CalendarService calendarService)
        {
            // Define parameters of request.
            EventsResource.ListRequest request = calendarService.Events.List("primary");
            request.TimeMinDateTimeOffset = DateTime.Now.AddMonths(-4);
            request.ShowDeleted = false;
            request.SingleEvents = true;
            request.MaxResults = 300;
            request.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;

            // List events.
            Events events = request.Execute();
            if (events.Items != null && events.Items.Count > 0)
            {
                Console.WriteLine($"Removing {events.Items.Count} event(s) from this calendar.");
                foreach (var eventItem in events.Items)
                {
                    try
                    {
                        Console.WriteLine($"Deleted eventId {eventItem.Id}");
                        calendarService.Events.Delete("primary", eventItem.Id).Execute();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.ToString());
                    }
                }
            }
            else
            {
                Console.WriteLine("Found no events on this account calendar.");
            }
        }

        List<VcbFieldEvent> fetchEvents(CalendarService calendarService, string location)
        {
            List<VcbFieldEvent> results = new();

            // Define parameters of request.
            EventsResource.ListRequest request = calendarService.Events.List("primary");
            request.TimeMinDateTimeOffset = DateTime.Now.AddMonths(-4);
            request.ShowDeleted = false;
            request.SingleEvents = true;
            request.MaxResults = 300;
            request.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;

            // List events.
            Events events = request.Execute();
            if (events.Items != null && events.Items.Count > 0)
            {
                results.Capacity = events.Items.Count;
                Console.WriteLine($"Found {events.Items.Count} event(s) on this calendar.");
                foreach (Event eventItem in events.Items)
                {
                    VcbFieldEvent vcbFieldEvent = new VcbFieldEvent();
                    vcbFieldEvent.location = location;
                    Match match = Program.SummaryRegex().Match(eventItem.Summary);
                    if (match.Success)
                    {
                        vcbFieldEvent.eventType = match.Groups["EventType"].Value == "Practice" ? VcbFieldEvent.Type.Practice : VcbFieldEvent.Type.Game;
                        vcbFieldEvent.homeTeam = match.Groups["homeTeam"].Value;
                    }
                    else
                    {
                        Console.WriteLine("Could not parse the summary for a Google event");
                    }

                    vcbFieldEvent.startTime = (eventItem.Start.DateTimeDateTimeOffset ?? DateTime.MinValue).DateTime;
                    vcbFieldEvent.endTime = (eventItem.End.DateTimeDateTimeOffset ?? DateTime.MinValue).DateTime;

                    if (vcbFieldEvent.eventType == VcbFieldEvent.Type.Practice) {
                        vcbFieldEvent.description = eventItem.Description;
                    }
                    else {
                        match = Program.DescriptionRegex().Match(eventItem.Description);
                        if (match.Success)
                        {
                            vcbFieldEvent.visitingTeam = match.Groups["visitingTeam"].Value ?? string.Empty;
                        }
                        else
                        {
                            Console.WriteLine("Could not parse the description for a Google event");
                        }
                    }

                    // Record the event Id in case we need to delete it
                    vcbFieldEvent.googleEventId = eventItem.Id;

                    results.Add(vcbFieldEvent);
                }
            }
            else
            {
                Console.WriteLine("Found no events on this account calendar.");
            }

            return results;
        }

        public void Reconcile()
        {
            foreach (string locationId in LOCATION_NAMES)
            {
                Console.WriteLine($"Processing events for location {locationId} ...");

                CalendarService calendarService = GetGoogleCalendarService(locationId);

                List<VcbFieldEvent> currentEvents = fetchEvents(calendarService, locationId);
                //DeleteAllCalendarEvents();
                //continue;

                // delete existing events from the Google calendar
                currentEvents.ForEach(e =>
                {
                    if (e.location == locationId && !mNewEventList.Contains(e))
                    {
                        try
                        {
                            calendarService.Events.Delete("primary", e.googleEventId).Execute();
                            Console.WriteLine($"Deleted event {e.eventType} at {e.location} on {e.startTime} for team {e.homeTeam}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex.ToString());
                        }
                    }
                });

                // insert new events into the Google calendar
                List<VcbFieldEvent> eventsToAdd = new List<VcbFieldEvent>();
                mNewEventList.ForEach(e =>
                {
                    if (e.location == locationId && !currentEvents.Contains(e)) {
                        addEventToGoogleCalendar(calendarService, e);
                    }
                });
            }
        }
    }
}
