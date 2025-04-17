using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VcbFieldExport {

    public class VcbFieldEvent : IEquatable<VcbFieldEvent>
    {
        public enum Type
        {
            Practice,
            Game,
        };
        
        public VcbFieldEvent()
        {
            // A default constructor is needed to load from the CSV database
            eventType = Type.Practice;
            location = string.Empty;
            startTime = DateTime.MinValue;
            homeTeam = string.Empty;
            visitingTeam = string.Empty;
            endTime = DateTime.MinValue;
            description = string.Empty;
            googleEventId = string.Empty;
            officialsRequired = false;
        }

        public VcbFieldEvent(Type eventType, string loc, DateTime start, string homeTeam, string visitingTeam, bool officialsRequired, DateTime end, string description = "", string googleEventId = "")
        {
            this.eventType = eventType;
            this.location = loc;
            this.startTime = start;
            this.homeTeam = homeTeam;
            this.visitingTeam = visitingTeam;
            // Games are always set to 2 hours in length.  Non-games can be any length of time
            this.endTime = eventType == Type.Game ? startTime.AddHours(2) : end;
            this.description = description;
            this.googleEventId = googleEventId;
            this.officialsRequired = officialsRequired;
        }

        public Type eventType { get; set; }
        public string location { get; set; }
        public DateTime startTime { get; set; }
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
