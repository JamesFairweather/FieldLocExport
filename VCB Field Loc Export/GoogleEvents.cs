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

namespace VcbFieldExport
{
    public class FieldEventMap : ClassMap<VcbFieldEvent>
    {
        public FieldEventMap()
        {
            Map(m => m.eventType).Index(0).Name("eventType");
            Map(m => m.location).Index(1).Name("location");
            Map(m => m.startTime).Index(2).Name("startTime");
            Map(m => m.homeTeam).Index(3).Name("homeTeam");
            Map(m => m.visitingTeam).Index(4).Name("visitingTeam");
            Map(m => m.endTime).Index(5).Name("endTime");
            Map(m => m.description).Index(6).Name("description");
            Map(m => m.googleEventId).Index(7).Name("googleEventId");
        }
    }

    // TODO:
    // * improve handling of failures from the Google service
    //   e.g. after a few days, the field account tokens expire and we get exceptions like this:
    // Google.Apis.Auth.OAuth2.Responses.TokenResponseException: Error: "invalid_grant", Description: "Token has been expired or revoked.", Uri: ""
    //    at Google.Apis.Auth.OAuth2.Responses.TokenResponse.FromHttpResponseAsync(HttpResponseMessage response, IClock clock, ILogger logger)
    // 
    // * there's probably some way to refresh the local tokens automatically.

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

        string SAVED_EVENT_FILENAME = "SavedEvents.csv";
        List<VcbFieldEvent> mExistingEvents = new();
        List<VcbFieldEvent> mNewEventList;

        public GoogleEvents(List<VcbFieldEvent> teamSnapEvents)
        {
            mNewEventList = teamSnapEvents;

            if (File.Exists(SAVED_EVENT_FILENAME)) {
                using (StreamReader reader = new StreamReader(SAVED_EVENT_FILENAME))
                {
                    using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
                    {
                        csv.Context.RegisterClassMap<FieldEventMap>();
                        mExistingEvents = csv.GetRecords<VcbFieldEvent>().ToList();
                    }
                }
            }
        }

        public void SaveEvents()
        {
            // Sort the event list by start time, then by location
            mNewEventList.Sort(
                delegate(VcbFieldEvent a, VcbFieldEvent b)
                {
                    int startTime = a.startTime.CompareTo(b.startTime);
                    if (startTime != 0) {
                        return startTime;
                    }

                    return a.location.CompareTo(b.location);
                });

            using (StreamWriter writer = new(SAVED_EVENT_FILENAME, false)) {
                using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture)) {
                    csv.WriteHeader<VcbFieldEvent>();
                    csv.NextRecord();
                    csv.WriteRecords(mNewEventList);
                }
            }
        }

        Google.Apis.Calendar.v3.CalendarService GetGoogleCalendarService(string locationName)
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

        void RemoveEventsFromGoogleCalendar(List<VcbFieldEvent> eventsToRemove)
        {
            foreach (VcbFieldEvent e in eventsToRemove)
            {
                var calendarId = "primary"; //Always primary.

                try
                {
                    mService.Events.Delete(calendarId, e.googleEventId).Execute();
                    Console.WriteLine($"Deleted eventId {e.googleEventId}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }

                Thread.Sleep(250);  // to comply with rate limits
            }
        }

        void PostNewEventsToGoogleCalendar(List<VcbFieldEvent> newEvents)
        {
            foreach (VcbFieldEvent e in newEvents)
            {
                Event googleCalendarEvent = new();

                googleCalendarEvent.Start = new EventDateTime()
                { DateTimeDateTimeOffset = e.startTime };

                googleCalendarEvent.End = new EventDateTime()
                { DateTimeDateTimeOffset = e.endTime };

                googleCalendarEvent.Summary = e.eventType.ToString() + " : " + e.homeTeam;
                googleCalendarEvent.Description = (e.eventType == VcbFieldEvent.Type.Practice ? e.description : "vs. " + e.visitingTeam);

                var calendarId = "primary"; //Always primary.

                Event result = new();

                try
                {
                    result = mService.Events.Insert(googleCalendarEvent, calendarId).Execute();
                    e.googleEventId = result.Id;
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }

                Thread.Sleep(250);  // to comply with rate limits
            }
        }

        // Uncomment these lines to remove all the events for a specific calendar
        // string removeAllEventsFromFieldCalendar = "Chaldecott Park N diamond";
        // DeleteAllCalendarEvents(GetGoogleCalendarService("Nanaimo Park N diamond"));
        // return 0;

        void DeleteAllCalendarEvents()
        {
            // Define parameters of request.
            EventsResource.ListRequest request = mService.Events.List("primary");
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
                        mService.Events.Delete("primary", eventItem.Id).Execute();
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

        public void Reconcile()
        {
            foreach (string locationId in LOCATION_NAMES)
            {
                Console.WriteLine($"Processing events for location {locationId} ...");

                mService = GetGoogleCalendarService(locationId);

                //DeleteAllCalendarEvents();
                //continue;

                // find events to remove and delete them from the Google calendar
                List<VcbFieldEvent> eventsToRemove = new List<VcbFieldEvent>();
                mExistingEvents.ForEach(e =>
                {
                    if (e.location == locationId && !mNewEventList.Contains(e))
                    {
                        if (e.googleEventId != string.Empty)
                        {
                            eventsToRemove.Add(e);
                        }
                        else
                        {
                            Console.WriteLine($"Warning event deleted that doesn't have an event ID ({locationId}: {e.eventType} {e.homeTeam}, {e.startTime}).  This event should be manually removed from the Google calendar and the CSV file.");
                        }
                    }
                });

                if (eventsToRemove.Count > 0)
                {
                    Console.WriteLine($"Removing {eventsToRemove.Count} event(s) from the calendar...");
                    RemoveEventsFromGoogleCalendar(eventsToRemove);
                }
                else
                {
                    Console.WriteLine($"No events to remove found for this location");
                }

                // find events to add and insert them into the Google calendar
                List<VcbFieldEvent> eventsToAdd = new List<VcbFieldEvent>();
                mNewEventList.ForEach(e =>
                {
                    if (e.location == locationId)
                    {
                        var existingEvent = mExistingEvents.Find(savedEvent => savedEvent.Equals(e));
                        if (existingEvent != null)
                        {
                            // copy the event Id from the old list to the new one
                            e.googleEventId = existingEvent.googleEventId;
                        }
                        else
                        {
                            // otherwise add it
                            eventsToAdd.Add(e);
                        }
                    }
                });

                if (eventsToAdd.Count > 0)
                {
                    Console.WriteLine($"Adding {eventsToAdd.Count} event(s) to the calendar...");
                    PostNewEventsToGoogleCalendar(eventsToAdd);
                }
                else
                {
                    Console.WriteLine($"No events to add found for this location");
                }
            }
        }

        Google.Apis.Calendar.v3.CalendarService? mService;
    }
}
