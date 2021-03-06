﻿using System.ComponentModel.DataAnnotations.Schema;

namespace Matrix.SynapseInterop.Database.Models
{
    [Table("room_memberships")]
    public class RoomMemberships
    {
        [Column("event_id")]
        public string EventId { get; set; }

        [Column("user_id")]
        public string UserId { get; set; }

        [Column("sender")]
        public string Sender { get; set; }

        [Column("room_id")]
        public string RoomId { get; set; }

        [Column("membership")]
        public string Membership { get; set; }
    }
}
