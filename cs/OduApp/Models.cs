// ODU C# models. Mirrors odu/models.py.
using System.Collections.Generic;

namespace OduApp
{
    public class Device
    {
        public string InstanceId;
        public string FriendlyName;
        public string DevClass;
        public string Manufacturer;
        public string Status;
        public int ProblemCode;
        public List<string> HwId;
        public string VenDev;
        public string DriverVersion;
        public string DriverDate;
        public string DriverProvider;

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
        public long Size;
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
