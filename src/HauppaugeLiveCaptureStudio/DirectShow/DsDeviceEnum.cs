using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using HauppaugeLiveCaptureStudio.Core;

namespace HauppaugeLiveCaptureStudio.DirectShow
{
    /// <summary>Finds the video capture devices (DirectShow "Video Input Device" category).</summary>
    internal static class DsDeviceEnum
    {
        /// <summary>Must be called on the MTA worker thread.</summary>
        public static List<CaptureDevice> Enumerate()
        {
            List<CaptureDevice> result = new List<CaptureDevice>();
            ICreateDevEnum devEnum = null;
            IEnumMoniker enumMoniker = null;

            try
            {
                devEnum = DsUtil.Create<ICreateDevEnum>(DsGuids.CLSID_SystemDeviceEnum);
                Guid category = DsGuids.CLSID_VideoInputDeviceCategory;
                int hr = devEnum.CreateClassEnumerator(ref category, out enumMoniker, 0);
                if (hr != 0 || enumMoniker == null) return result;      // S_FALSE: no devices at all

                IMoniker[] monikers = new IMoniker[1];
                while (enumMoniker.Next(1, monikers, IntPtr.Zero) == 0)
                {
                    IMoniker m = monikers[0];
                    monikers[0] = null;
                    try
                    {
                        string id;
                        m.GetDisplayName(null, null, out id);
                        string name = ReadFriendlyName(m);
                        if (string.IsNullOrEmpty(name)) name = "Video device";
                        result.Add(new CaptureDevice { Id = id, Name = name });
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn("Skipping a video device that could not be read: " + ex.Message);
                    }
                    finally
                    {
                        DsUtil.ReleaseOne(m);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Device enumeration failed", ex);
            }
            finally
            {
                DsUtil.Release(enumMoniker);
                DsUtil.Release(devEnum);
            }

            // Two identical devices would show the same name -> number them.
            Dictionary<string, int> seen = new Dictionary<string, int>();
            foreach (CaptureDevice d in result)
            {
                int n;
                seen.TryGetValue(d.Name, out n);
                seen[d.Name] = n + 1;
                if (n > 0) d.Name = d.Name + " #" + (n + 1);
            }
            return result;
        }

        /// <summary>Creates the capture filter of the device with the given id. MTA worker thread only.</summary>
        public static IBaseFilter BindFilter(string deviceId)
        {
            ICreateDevEnum devEnum = null;
            IEnumMoniker enumMoniker = null;
            try
            {
                devEnum = DsUtil.Create<ICreateDevEnum>(DsGuids.CLSID_SystemDeviceEnum);
                Guid category = DsGuids.CLSID_VideoInputDeviceCategory;
                int hr = devEnum.CreateClassEnumerator(ref category, out enumMoniker, 0);
                if (hr != 0 || enumMoniker == null)
                    throw new CaptureException("No video capture devices are installed.", hr);

                IMoniker[] monikers = new IMoniker[1];
                while (enumMoniker.Next(1, monikers, IntPtr.Zero) == 0)
                {
                    IMoniker m = monikers[0];
                    monikers[0] = null;
                    try
                    {
                        string id;
                        m.GetDisplayName(null, null, out id);
                        if (!string.Equals(id, deviceId, StringComparison.OrdinalIgnoreCase)) continue;

                        Guid iid = typeof(IBaseFilter).GUID;
                        object obj;
                        m.BindToObject(null, null, ref iid, out obj);
                        return (IBaseFilter)obj;
                    }
                    finally
                    {
                        DsUtil.ReleaseOne(m);
                    }
                }
                throw new CaptureException("The selected capture device is not connected.");
            }
            catch (COMException ex)
            {
                throw new CaptureException("Could not open the capture device: " + DsUtil.DescribeHr(ex.HResult), ex.HResult, ex);
            }
            finally
            {
                DsUtil.Release(enumMoniker);
                DsUtil.Release(devEnum);
            }
        }

        private static string ReadFriendlyName(IMoniker moniker)
        {
            object bagObj = null;
            try
            {
                Guid iid = typeof(IPropertyBag).GUID;
                moniker.BindToStorage(null, null, ref iid, out bagObj);
                IPropertyBag bag = (IPropertyBag)bagObj;
                object value;
                if (bag.Read("FriendlyName", out value, IntPtr.Zero) == 0) return value as string;
            }
            catch
            {
            }
            finally
            {
                DsUtil.Release(bagObj);
            }
            return null;
        }
    }
}
