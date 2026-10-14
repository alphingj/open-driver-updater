// ODU C# models. Mirrors odu/models.py. C# 5 syntax (Framework csc).
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace OduApp
{
    [DataContract]
    public class Device
    {
        [DataMember(Name = "instance_id")] public string InstanceId;
        [DataMember(Name = "friendly_name")] public string FriendlyName;
        [DataMember(Name = "dev_class")] public string DevClass;
        [DataMember(Name = "manufacturer")] public string Manufacturer;
        [DataMember(Name = "status")] public string Status;
        [DataMember(Name = "problem_code")] public int ProblemCode;
        [DataMember(Name = "hwid")] public List<string> HwId;
        [DataMember(Name = "ven_dev")] public string VenDev;
        [DataMember(Name = "driver_version")] public string DriverVersion;
        [DataMember(Name = "driver_date")] public string DriverDate;
        [DataMember(Name = "driver_provider")] public string DriverProvider;

        public string PrimaryHwId()
        {
            if (HwId != null && HwId.Count > 0) return HwId[0];
            return "";
        }

        public string DisplayName()
        {
            if (!string.IsNullOrEmpty(FriendlyName)) return FriendlyName;
            return InstanceId;
        }
    }

    public class DriverUpdate
    {
        public string HwId;
        public string DeviceName;
        public string InstanceId;
        public string InstalledVersion;
        public string AvailableVersion;
        public string Source;
        public string DownloadUrl;
        public string Arch;
        public string Title;
        public string UpdateId;
        public string Vendor;
        public string VendorUrl;
    }

    public class JobItem
    {
        public string Device;
        public string HwId;
        public bool Ok;
        public string Detail;
    }
}
