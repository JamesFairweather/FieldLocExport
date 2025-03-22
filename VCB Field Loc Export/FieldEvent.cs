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
        }

        public VcbFieldEvent(Type _eventType, string _loc, DateTime _start, string _homeTeam, string _visitingTeam, DateTime _end, string _description = "", string _googleEventId = "")
        {
            eventType = _eventType;
            location = _loc;
            startTime = _start;
            homeTeam = _homeTeam;
            visitingTeam = _visitingTeam;
            // Games are always set to 2 hours in length.  Non-games can be any length of time
            endTime = eventType == Type.Game ? startTime.AddHours(2) : _end;
            description = _description;
            googleEventId = _googleEventId;
        }

        public Type eventType { get; set; }
        public string location { get; set; }
        public DateTime startTime { get; set; }
        public string homeTeam { get; set; }
        public string visitingTeam { get; set; }
        public DateTime endTime { get; set; }
        public string description { get; set; }
        public string googleEventId { get; set; }

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
