using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace VcbFieldExport {

    public class VcbFieldEvent : IEquatable<VcbFieldEvent>
    {
        public enum Type
        {
            Practice,
            Game,
            PlayoffGame,
        };
        
        public VcbFieldEvent()
        {
            // A default constructor is needed to load from the CSV database
            eventType = Type.Practice;
            location = string.Empty;
            startTime = DateTime.MinValue;
            division = string.Empty;
            homeTeam = string.Empty;
            visitingTeam = string.Empty;
            endTime = DateTime.MinValue;
            description = string.Empty;
            googleEventId = string.Empty;
            officialsRequired = false;
        }

        public VcbFieldEvent(Type eventType, string loc, DateTime start, string division, string homeTeam, string visitingTeam, string description, bool officialsRequired)
        {
            this.eventType = eventType;
            this.location = loc;
            this.startTime = start;
            this.division = division;
            this.homeTeam = homeTeam;
            this.visitingTeam = visitingTeam;
            this.endTime = startTime.AddHours(2);
            this.description = description;
            this.googleEventId = string.Empty;
            this.officialsRequired = officialsRequired;
        }
        public VcbFieldEvent(string loc, DateTime start, DateTime end, string team, string description)
        {
            this.eventType = Type.Practice;
            this.location = loc;
            this.startTime = start;
            this.endTime = end;
            this.homeTeam = team;
            this.description = description;

            this.division = string.Empty;
            this.visitingTeam = string.Empty;
            this.googleEventId = string.Empty;
            this.officialsRequired = false;
        }

        public Type eventType { get; set; }
        public string location { get; set; }
        public DateTime startTime { get; set; }
        public string division { get; set; }
        public string homeTeam { get; set; }
        public string visitingTeam { get; set; }
        public DateTime endTime { get; set; }
        public string description { get; set; }
        public string googleEventId { get; set; }
        public bool officialsRequired { get; set; }

        public override bool Equals(object? obj)
        {
            return this.Equals(obj as VcbFieldEvent);
        }

        public bool Equals(VcbFieldEvent? e)
        {
            if (e is null)
            {
                return false;
            }

            if (Object.ReferenceEquals(this, e))
            {
                return true;
            }

            return eventType == e.eventType &&
                location == e.location &&
                startTime == e.startTime &&
                homeTeam == e.homeTeam &&
                visitingTeam == e.visitingTeam;
        }

        public override int GetHashCode() => base.GetHashCode();
    };
}
