﻿using Glimmr.Models;
using Newtonsoft.Json;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Xamarin.Forms;

namespace Glimmr
{
    enum DeviceStatus { Default, Unreachable, Error };

    //Data Model. Represents a Glimmr light with a network address, name, and some current light values.
    [XmlType("dev")]
    public class GlimmrDevice : INotifyPropertyChanged, IComparable
    {
        private string networkAddress = "10.41.0.1";                          //device IP (can also be hostname if applicable)
        private string name = "";                                               //device display name ("Server Description")
        private DeviceStatus status = DeviceStatus.Default;                     //Current connection status
        private bool stateCurrent = false;                                      //Is the light currently on?
        private bool isEnabled = true;                                          //Disabled devices don't get polled or show up in the list
        private double brightnessReceived = 0.9, brightnessCurrent = 0.9;       //There are two vars for brightness to discern API responses from slider updates

        [XmlElement("url")]
        public string NetworkAddress
        {
            set
            {
                if (value == null || value.Length < 3) return; //More elaborate checking for URL syntax could be added here
                networkAddress = value;
            }
            get { return networkAddress; }
        }

        [XmlElement("name")]
        public string Name
        { 
            set
            {
                if (value == null || name.Equals(value)) return; //Make sure name is not set to null
                name = value;
                OnPropertyChanged("Name");
            }
            get { return name; }
        }

        internal DeviceStatus CurrentStatus
        {
            set
            {
                status = value;
                OnPropertyChanged("Status");
            }
            get { return status; }
        }

        [XmlElement("ncustom")]
        public bool NameIsCustom { get; set; } = true; //If the light name is custom, the name returned by the API response will be ignored

        [XmlElement("en")]
        public bool IsEnabled
        {
            set
            {
                isEnabled = value;
                OnPropertyChanged("Status");
                OnPropertyChanged("ListHeight");
                OnPropertyChanged("TextColor");
                OnPropertyChanged("IsEnabled");
            }
            get { return isEnabled; }
        }

        [XmlIgnore]
        public double BrightnessCurrent
        {
            set
            {
                brightnessCurrent = value;
                if (brightnessCurrent != brightnessReceived) //only send if value was changed by slider
                {
                    byte toSend = (byte)Math.Round(brightnessCurrent);
                    RateLimitedSender.SendAPICall(this, "/brightness?value=" + toSend);
                }
            }
            get { return brightnessCurrent; }
        }

        [XmlIgnore]
        public Color ColorCurrent { get; set; }

        [XmlIgnore]
        public bool StateCurrent
        {
            get { return stateCurrent; }
            set { OnPropertyChanged("StateColor"); stateCurrent = value; }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        //helper properties for updating view dynamically via data binding
        [XmlIgnore]
        public Color StateColor { get { return StateCurrent ? Color.FromHex("#666") : Color.FromHex("#222"); } } //button background color

        [XmlIgnore]
        public string ListHeight { get { return isEnabled ? "-1" : "0"; } } //height of one view cell (set to 0 to hide device)

        [XmlIgnore]
        public string TextColor { get { return isEnabled ? "#FFF" : "#999"; } } //text color for modification page

        [XmlIgnore]
        public string Status //string containing IP and current status, second label in list viewcell
        {
            get
            {
                string statusText = "";
                if (IsEnabled)
                {
                    switch (status)
                    {
                        case DeviceStatus.Default: statusText = ""; break;
                        case DeviceStatus.Unreachable: statusText = " (Offline)"; break;
                        case DeviceStatus.Error: statusText = " (Error)"; break;
                    }
                }
                else
                {
                    statusText = " (Hidden)";
                }
                return string.Format("{0}{1}", networkAddress, statusText);
            }
        }

        //constructors
        public GlimmrDevice() { }

        public GlimmrDevice(string nA, string name)
        {
            NetworkAddress = nA;
            Name = name;
        }

        //member functions

        //send a call to this device's Glimmr HTTP API
        public async Task<bool> SendApiCall(string call)
        {
            string url = "http://" + networkAddress;
            if (networkAddress.StartsWith("https://"))
            {
                url = networkAddress;
            }
            Debug.WriteLine("URL: " + url);
            string response = await DeviceHttpConnection.GetInstance().Send_Glimmr_API_Call(url, call);
            if (response == null)
            {
                Debug.WriteLine("NO RESPONSE.");
                CurrentStatus = DeviceStatus.Unreachable;
                return false;
            }

            if (response.Equals("err")) //404 or other non-success http status codes, indicates that target is not a Glimmr device
            {
                Debug.WriteLine("Response error.");
                CurrentStatus = DeviceStatus.Error;
                return false;
            }

            SystemData deviceResponse = JsonConvert.DeserializeObject<SystemData>(response);
            if (deviceResponse == null) //could not parse XML API response
            {
                Debug.WriteLine("Response is null.");
                CurrentStatus = DeviceStatus.Error;
                return false;
            } else
            {
                Debug.WriteLine("We have a valid response: " + JsonConvert.SerializeObject(deviceResponse));
            }

            CurrentStatus = DeviceStatus.Default; //the received response was valid

            if (!NameIsCustom) Name = deviceResponse.Name;

            //only consider brightness if light is on and if it wasn't modified in the same call (prevents brightness slider "jumps")
            if (deviceResponse.Brightness > 0 && !call.Contains("A="))
            {
                brightnessReceived = deviceResponse.Brightness;
                BrightnessCurrent = brightnessReceived;
                OnPropertyChanged("BrightnessCurrent"); //update slider binding
            }

            ColorCurrent = Color.FromHex(deviceResponse.AmbientColor);
            OnPropertyChanged("ColorCurrent");

            StateCurrent = deviceResponse.DeviceMode != 0;
            Debug.WriteLine("Returning true?");
            return true;
        }

        public async Task<bool> Refresh() //fetches updated values from Glimmr device
        {
            if (!IsEnabled) return false;
            return await SendApiCall("");
        }

        public int CompareTo(object comp) //compares devices in alphabetic order based on name
        {
            GlimmrDevice c = comp as GlimmrDevice;
            if (c == null || c.Name == null) return 1;
            int result = (name.CompareTo(c.name));
            if (result != 0) return result;
            return (networkAddress.CompareTo(c.networkAddress));
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
