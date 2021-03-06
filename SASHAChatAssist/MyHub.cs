﻿using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNet.SignalR;
using Newtonsoft.Json;
using System.Web;

namespace SASHAChatAssist
{

    public static class groupNames
    {
        public const string Monitor = "Monitor";
        public const string Sasha = "Sasha";
    }

    public class MyHub : Hub
    {
        /* ***** GENERIC METHODS ***** */

        /* Send a message to the browser console */
        public void Debug(string message)
        {
            Clients.Caller.debug(message);
        }

        /* Update chat with chat messages */
        public void BroadcastMessage(string chatId, string message)
        {
            string name = Clients.Caller.userName;
            string time = System.DateTime.UtcNow.ToString("yyyy-MM-ddTHH\\:mm\\:ssZ");
            if (chatId != groupNames.Monitor)
            {
                Database.SaveChatLog(chatId, time, name, message);
            }
            Clients.Group(chatId).broadcastMessage(chatId, time, name, message);
        }

        /* Broadcasts an annoucement from the Monitors */
        public void BroadcastAnnouncement(string announcement, string groupName)
        {
            string name = Clients.Caller.userName;
            string id = Clients.Caller.userId;
            name = name + "(" + id + "): ";
            announcement = name + announcement;
            if (groupName == "All")
            {
                Clients.Group(groupNames.Sasha).throwMessage("Announcement", announcement, false);
            } else
            {
                Clients.Group(groupName).throwMessage("Announcement", announcement, false);
            }
            string time = System.DateTime.UtcNow.ToString("yyyy-MM-ddTHH\\:mm\\:ssZ");
            Clients.Group(groupNames.Monitor).broadcastMessage(groupNames.Monitor, time, "BROADCAST MESSAGE", announcement);
        }

        /* Check Database for any unseen announcements */
        public void CheckAnnouncements(string userId)
        {
            string announcements = Database.checkAnnouncements(userId);
            Clients.Caller.showUnseenAnnouncement(announcements);
        }

        /* ***** MONITOR SPECIFIC FUNCTIONS ***** */

        /* Returns True if the user is authenticated, false if not */
        public void CheckAuthenticated()
        {
            if (Context.User.Identity.IsAuthenticated)
            {
                RegisterMonitor();
            }
            else
            {
                Clients.Caller.getUserId();
            }
        }

        /* Gather required information for monitor clients and update records if necessary */
        public void RegisterMonitor()
        {

            /* If the user is authenticated it retrieves the userId value from the authenticated information.
                If the user is not authenticated it retrieves the value from the userId sent with the command */
            if (Context.User.Identity.IsAuthenticated) { 
                string id = Context.User.Identity.Name.GetUserId();
                Clients.Caller.userId = id;
            }

            string userId = Clients.Caller.userId;
            /* Retrieve the userName from the database for the userId given */
            string userName = Database.GetUserName(userId, "");

            /* If userName is not present set it to the userId */
            if (userName == null)
            {
                userName = userId;
            }

            /* Store the userId and userName on the connection for easy retrieval */
            Clients.Caller.userId = userId;
            Clients.Caller.userName = userName;

            /* Update information in the chatHelper table for the user */
            string chatHelper = Database.GetChatHelper(userId, Context.ConnectionId);

            /* No chatHelper record exists, display an error */
            if (chatHelper == "noRecord")
            {
                Clients.Caller.throwMessage("User Not Found","<p>No user record for ATTUID: '<b>" + userId + "</b>' found.</p><p>Unable to connect as a chatHelper at this time.</p>", true);
                return;
            }

            /* User shows connected in chatHelper, display an error */
            if (chatHelper == "existingConnection")
            {
                Clients.Caller.throwMessage("User Already Connected","<p>ATTUID: '<b>" + userId + "</b>' has an existing connection.</p><p>Please try again in a few minutes.</p>", true);
                return;
            }

            /* Add the connection to the group Monitor */
            Groups.Add(Context.ConnectionId, groupNames.Monitor);

            /* Add the connection to a group based on userId */
            Groups.Add(Context.ConnectionId, userId);

            /* Get Location Names From Location Codes */
            string locationCodes = Database.GetLocationCodes();

            /* Update the userName div on the main page with the recovered userName and send permissions */
            Clients.Caller.updateDisplay(userName, chatHelper, locationCodes);

            /* Get QueryString of URL */
            var url = HttpContext.Current.Request.Url.Query;
            var query = HttpUtility.ParseQueryString(url);
            var instance = query.Get("instance");
            if (instance.ToLower() == "staging")
            {
                instance = "Staging";
            } else
            {
                instance = "Prod";
            }

            /* Retrieve list of Sasha Sessions from the sashaSessions and push them to the newly connected monitor */
            string sashaSessionRecords = Database.GetSashaSessionRecords(instance);
            Clients.Caller.receiveSashaSessionRecords(sashaSessionRecords);

        }

        /* Update Monitor clients with activity and milestone information */
        public void UpdateMonitor(string milestone, string lastAgentActivityTime)
        {
            string connectionId = Context.ConnectionId;
            Clients.Group(groupNames.Monitor).updateMonitor(connectionId, milestone, lastAgentActivityTime);
        }

        /* Updates the Toggle Helper Status to the correct value */
        public void ToggleHelperStatus(string status)
        {
            Database.ToggleHelperStatus(Context.ConnectionId, status);
        }

        /* ***** SASHA SPECIFIC FUNCTIONS ***** */

        /* Add Initial Record to SASHA Sessions */
        public void RegisterSashaSession(string userId, string agentName, string smpSessionId, string agentLocationCode, string instance)
        {
            string userid = userId.ToLower();
            string connectionId = Context.ConnectionId;
            string userName = Database.GetUserName(userId,agentLocationCode);
            if (userName == null)
            {
                Database.AddUserRecord(userId.ToLower(), agentName.ToUpper(), agentLocationCode.ToUpper());
            }
            if (agentLocationCode != "" && agentLocationCode != null)
            {
                Groups.Add(connectionId, agentLocationCode);
            }
            Clients.Caller.userId = userId.ToLower();
            Clients.Caller.userName = agentName.ToUpper();
            Clients.Caller.smpSessionId = smpSessionId;
            Groups.Add(connectionId, smpSessionId);
            string sessionStartTime = "";
            if (Database.AddSashaSessionRecord(connectionId, userId, smpSessionId, sessionStartTime, "", instance))
            {
                Groups.Add(connectionId, groupNames.Sasha);
                Groups.Add(connectionId, smpSessionId);
                Groups.Add(connectionId, instance);
                Clients.Group(groupNames.Monitor).addSashaSession(connectionId, userId, userName, sessionStartTime, "");
                CheckAnnouncements(userId);
            }
        }

        /* Sets the Session Start Time to a value indicating that you have begun the actual SASHA flow and should be tracked */
        public void UpdateSashaSession(string milestone, string lastAgentActivityTime)
        {
            string userId = Clients.Caller.userId;
            string connectionId = Context.ConnectionId;
            Database.UpdateSashaSessionRecord(userId, connectionId, milestone, lastAgentActivityTime);
        }

        /* Sasha user requests a chat */
        public void SashaInitiateChat(string smpSessionId, string flowName, string stepName)
        {
            /* User Id of agent requesting Chat */
            string userId = Clients.Caller.userId;
            /* UserName of the agent requesting Chat */
            string userName = Clients.Caller.userName;
            /* ConnectionId of session requesting chat */
            string connectionId = Context.ConnectionId;
            Database.GetAvailableHelper(smpSessionId, userId, userName, connectionId, flowName, stepName);
            Clients.Caller.openChatWindow();
        }

        /* Opens the chat Window after succesful chat connection */
        public void OpenChatWindow()
        {

        }

        /* Enables Opt Out Option on SASHA Client */
        public void EnableOptOut(string connectionId)
        {
            Clients.Client(connectionId).enableOptOut(Clients.Caller.userName);
        }

        /* When a client disconnects attempts to remove its record from the SashaSessions Database and calls for an update of sasha sessions on all monitor clients */
        public override Task OnDisconnected(bool stopCalled)
        {
            string connectionId = Context.ConnectionId;
            if (Database.RemoveSashaSessionRecord(connectionId))
            {
                Clients.Group(groupNames.Monitor).removeSashaSession(connectionId);
            }
            Database.UpdateChatHelper(connectionId);
            return base.OnDisconnected(stopCalled);
        }

        /* Request SASHA Dictionary Data Retrieval */
        public void PullSashaData(string gatherFromConnection, List<string> fields)
        {
            string sendToConnection = Context.ConnectionId;
            string sendToConnectionName = Clients.Caller.userId;
            Clients.Client(gatherFromConnection).gatherSashaData(sendToConnection, fields);
        }

        /* Receive SASHA Dictionary Data */
        public void PushSashaData(string sendTo, string smpSessionId, string jsonData)
        {
            string receiveFromName = Clients.Caller.userId;
            Clients.Client(sendTo).receiveSashaData(smpSessionId, jsonData);
        }



        /* Request SASHA ScreenShot */
        public void PullSashaScreenShot(string fromConnection)
        {
            string toConnection = Context.ConnectionId;
            Clients.Client(fromConnection).requestSashaScreenShot(toConnection);
        }

        /* Receive SASHA ScreenShot */
        public void PushSashaScreenShot(string toConnection, string image)
        {
            string fromConnection = Clients.Caller.ConnectionId;
            Clients.Client(toConnection).receiveSashaScreenShot(fromConnection, image);
        }




        /* Remotely request saving of the dictionary */
        public void SaveDictionary(string connectionId)
        {
            string name = Clients.Caller.userId;
            string requestId = Context.ConnectionId;
            Clients.Client(connectionId).saveDictionary(requestId);
        }

        /* Remotely initiate a chat window to a SASHA user */
        public void RequestChat(string connectionId)
        {
            string requesterConnectionId = Context.ConnectionId;
            string requesterName = Clients.Caller.userName;
            Database.pushChat(connectionId, requesterConnectionId, requesterName);
        }

        /* Send Notification back to requester of dictionary name saved */
        public void NotifyDictionarySaved(string connectionId, string dictionaryName)
        {
            Clients.Client(connectionId).throwMessage("Dictionary Saved", "Dictionary has been saved with the name of " + dictionaryName, false);
        }
    }
}