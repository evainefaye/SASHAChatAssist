//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated from a template.
//
//     Manual changes to this file may cause unexpected behavior in your application.
//     Manual changes to this file will be overwritten if the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace SASHAChatAssist
{
    using System;
    using System.Collections.Generic;
    
    public partial class sashaSession
    {
        public string connectionId { get; set; }
        public string userId { get; set; }
        public string sessionStartTime { get; set; }
        public string smpSessionId { get; set; }
        public string milestone { get; set; }
    
        public virtual user user { get; set; }
    }
}
