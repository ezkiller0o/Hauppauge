using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

// Minimal hand-written DirectShow COM interop (no third-party package needed).
// Only the interfaces that the capture engine really uses are declared.
// IMPORTANT: the ORDER of the methods inside every interface is the COM vtable order - do not reorder.
// Methods that are never called are declared with IntPtr parameters so no marshalling can go wrong.

namespace HauppaugeLiveCaptureStudio.DirectShow
{
    internal static class DsGuids
    {
        public static readonly Guid CLSID_FilterGraph = new Guid("e436ebb3-524f-11ce-9f53-0020af0ba770");
        public static readonly Guid CLSID_CaptureGraphBuilder2 = new Guid("bf87b6e1-8c27-11d0-b3f0-00aa003761c5");
        public static readonly Guid CLSID_SampleGrabber = new Guid("c1f400a0-3f08-11d3-9f0b-006008039e37");
        public static readonly Guid CLSID_NullRenderer = new Guid("c1f400a4-3f08-11d3-9f0b-006008039e37");
        public static readonly Guid CLSID_SystemDeviceEnum = new Guid("62be5d10-60eb-11d0-bd3b-00a0c911ce86");
        public static readonly Guid CLSID_VideoInputDeviceCategory = new Guid("860bb310-5d01-11d0-bd3b-00a0c911ce86");

        public static readonly Guid PIN_CATEGORY_CAPTURE = new Guid("fb6c4281-0353-11d1-905f-0000c0cc16ba");
        public static readonly Guid PIN_CATEGORY_PREVIEW = new Guid("fb6c4282-0353-11d1-905f-0000c0cc16ba");

        public static readonly Guid MEDIATYPE_Video = new Guid("73646976-0000-0010-8000-00aa00389b71");
        public static readonly Guid MEDIASUBTYPE_RGB24 = new Guid("e436eb7d-524f-11ce-9f53-0020af0ba770");
        public static readonly Guid MEDIASUBTYPE_RGB32 = new Guid("e436eb7e-524f-11ce-9f53-0020af0ba770");

        public static readonly Guid FORMAT_VideoInfo = new Guid("05589f80-c356-11ce-bf01-00aa0055595a");
        public static readonly Guid FORMAT_VideoInfo2 = new Guid("f72a76a0-eb0a-11d0-ace4-0000c0cc16ba");

        public static readonly Guid IID_IAMStreamConfig = new Guid("c6e13340-30ac-11d0-a18c-00a0c9118956");
        public static readonly Guid IID_IAMBufferNegotiation = new Guid("56ed71a0-af5f-11d0-b3f0-00aa003761c5");
    }

    /// <summary>PhysicalConnectorType values reported by IAMCrossbar.</summary>
    internal static class PhysConn
    {
        public const int VideoTuner = 1;
        public const int VideoComposite = 2;
        public const int VideoSVideo = 3;
        public const int VideoRGB = 4;
        public const int VideoYRYBY = 5;
        public const int VideoAUX = 9;
        public const int VideoVideoDecoder = 12;
        public const int VideoSCART = 14;
        public const int AudioFirst = 0x1000;     // every value >= 0x1000 is an audio connector
    }

    /// <summary>Filter-graph event codes (evcode.h).</summary>
    internal static class EventCodes
    {
        public const int EC_ERRORABORT = 0x03;
        public const int EC_STREAM_ERROR_STOPPED = 0x06;
        public const int EC_DEVICE_LOST = 0x1F;
        public const int EC_ERRORABORTEX = 0x45;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AMMediaType
    {
        public Guid MajorType;
        public Guid SubType;
        public int FixedSizeSamples;        // BOOL
        public int TemporalCompression;     // BOOL
        public int SampleSize;
        public Guid FormatType;
        public IntPtr Unknown;
        public int FormatSize;
        public IntPtr FormatPtr;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AllocatorProperties
    {
        public int cBuffers;
        public int cbBuffer;
        public int cbAlign;
        public int cbPrefix;
    }

    // ------------------------------------------------------------------ clocks / graph

    [ComImport, Guid("56a86897-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IReferenceClock
    {
        // never called - only used as a type so that SetSyncSource(null) can be expressed
    }

    [ComImport, Guid("0000010c-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPersist
    {
        [PreserveSig] int GetClassID(out Guid pClassID);
    }

    [ComImport, Guid("56a86899-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMediaFilter : IPersist
    {
        [PreserveSig] int Stop();
        [PreserveSig] int Pause();
        [PreserveSig] int Run(long tStart);
        [PreserveSig] int GetState(int dwMilliSecsTimeout, out int filterState);
        [PreserveSig] int SetSyncSource([In, MarshalAs(UnmanagedType.Interface)] IReferenceClock pClock);
        [PreserveSig] int GetSyncSource([MarshalAs(UnmanagedType.Interface)] out IReferenceClock pClock);
    }

    [ComImport, Guid("56a86895-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IBaseFilter : IMediaFilter
    {
        [PreserveSig] int EnumPins([MarshalAs(UnmanagedType.Interface)] out IEnumPins ppEnum);
        [PreserveSig] int FindPin([In, MarshalAs(UnmanagedType.LPWStr)] string id, [MarshalAs(UnmanagedType.Interface)] out IPin ppPin);
        [PreserveSig] int QueryFilterInfo(IntPtr pInfo);
        [PreserveSig] int JoinFilterGraph([In, MarshalAs(UnmanagedType.Interface)] IFilterGraph pGraph, [In, MarshalAs(UnmanagedType.LPWStr)] string pName);
        [PreserveSig] int QueryVendorInfo(out IntPtr pVendorInfo);
    }

    [ComImport, Guid("56a86892-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IEnumPins
    {
        [PreserveSig] int Next(int cPins, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] IPin[] ppPins, IntPtr pcFetched);
        [PreserveSig] int Skip(int cPins);
        [PreserveSig] int Reset();
        [PreserveSig] int Clone([MarshalAs(UnmanagedType.Interface)] out IEnumPins ppEnum);
    }

    [ComImport, Guid("56a86891-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPin
    {
        [PreserveSig] int Connect([In, MarshalAs(UnmanagedType.Interface)] IPin pReceivePin, IntPtr pmt);
        [PreserveSig] int ReceiveConnection([In, MarshalAs(UnmanagedType.Interface)] IPin pConnector, IntPtr pmt);
        [PreserveSig] int Disconnect();
        [PreserveSig] int ConnectedTo([MarshalAs(UnmanagedType.Interface)] out IPin ppPin);
        [PreserveSig] int ConnectionMediaType(IntPtr pmt);
        [PreserveSig] int QueryPinInfo(IntPtr pInfo);
        [PreserveSig] int QueryDirection(out int pPinDir);
        [PreserveSig] int QueryId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int QueryAccept(IntPtr pmt);
        [PreserveSig] int EnumMediaTypes(out IntPtr ppEnum);
        [PreserveSig] int QueryInternalConnections(IntPtr apPin, ref int nPin);
        [PreserveSig] int EndOfStream();
        [PreserveSig] int BeginFlush();
        [PreserveSig] int EndFlush();
        [PreserveSig] int NewSegment(long tStart, long tStop, double dRate);
    }

    [ComImport, Guid("56a86893-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IEnumFilters
    {
        [PreserveSig] int Next(int cFilters, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] IBaseFilter[] ppFilter, IntPtr pcFetched);
        [PreserveSig] int Skip(int cFilters);
        [PreserveSig] int Reset();
        [PreserveSig] int Clone([MarshalAs(UnmanagedType.Interface)] out IEnumFilters ppEnum);
    }

    [ComImport, Guid("56a8689f-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IFilterGraph
    {
        [PreserveSig] int AddFilter([In, MarshalAs(UnmanagedType.Interface)] IBaseFilter pFilter, [In, MarshalAs(UnmanagedType.LPWStr)] string pName);
        [PreserveSig] int RemoveFilter([In, MarshalAs(UnmanagedType.Interface)] IBaseFilter pFilter);
        [PreserveSig] int EnumFilters([MarshalAs(UnmanagedType.Interface)] out IEnumFilters ppEnum);
        [PreserveSig] int FindFilterByName([In, MarshalAs(UnmanagedType.LPWStr)] string pName, [MarshalAs(UnmanagedType.Interface)] out IBaseFilter ppFilter);
        [PreserveSig] int ConnectDirect([In, MarshalAs(UnmanagedType.Interface)] IPin ppinOut, [In, MarshalAs(UnmanagedType.Interface)] IPin ppinIn, IntPtr pmt);
        [PreserveSig] int Reconnect([In, MarshalAs(UnmanagedType.Interface)] IPin ppin);
        [PreserveSig] int Disconnect([In, MarshalAs(UnmanagedType.Interface)] IPin ppin);
        [PreserveSig] int SetDefaultSyncSource();
    }

    [ComImport, Guid("56a868a9-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IGraphBuilder : IFilterGraph
    {
        [PreserveSig] int Connect([In, MarshalAs(UnmanagedType.Interface)] IPin ppinOut, [In, MarshalAs(UnmanagedType.Interface)] IPin ppinIn);
        [PreserveSig] int Render([In, MarshalAs(UnmanagedType.Interface)] IPin ppinOut);
        [PreserveSig] int RenderFile([In, MarshalAs(UnmanagedType.LPWStr)] string strFile, [In, MarshalAs(UnmanagedType.LPWStr)] string strPlayList);
        [PreserveSig] int AddSourceFilter([In, MarshalAs(UnmanagedType.LPWStr)] string strFileName, [In, MarshalAs(UnmanagedType.LPWStr)] string strFilterName, [MarshalAs(UnmanagedType.Interface)] out IBaseFilter ppFilter);
        [PreserveSig] int SetLogFile(IntPtr hFile);
        [PreserveSig] int Abort();
        [PreserveSig] int ShouldOperationContinue();
    }

    [ComImport, Guid("93e5a4e0-2d50-11d2-abfa-00a0c9c6e38d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ICaptureGraphBuilder2
    {
        [PreserveSig] int SetFiltergraph([In, MarshalAs(UnmanagedType.Interface)] IGraphBuilder pfg);
        [PreserveSig] int GetFiltergraph([MarshalAs(UnmanagedType.Interface)] out IGraphBuilder ppfg);
        [PreserveSig] int SetOutputFileName(IntPtr pType, [In, MarshalAs(UnmanagedType.LPWStr)] string lpstrFile, out IntPtr ppbf, out IntPtr ppSink);
        [PreserveSig] int FindInterface(IntPtr pCategory, IntPtr pType, [In, MarshalAs(UnmanagedType.Interface)] IBaseFilter pbf, [In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppint);
        [PreserveSig] int RenderStream(IntPtr pCategory, IntPtr pType, [In, MarshalAs(UnmanagedType.IUnknown)] object pSource, [In, MarshalAs(UnmanagedType.Interface)] IBaseFilter pfCompressor, [In, MarshalAs(UnmanagedType.Interface)] IBaseFilter pfRenderer);
        // ControlStream, AllocCapFile, CopyCaptureFile and FindPin follow in the real vtable but are not used here.
    }

    // ------------------------------------------------------------------ run control & events (dual interfaces)

    [ComImport, Guid("56a868b1-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    internal interface IMediaControl
    {
        [PreserveSig] int Run();
        [PreserveSig] int Pause();
        [PreserveSig] int Stop();
        [PreserveSig] int GetState(int msTimeout, out int pfs);
        // RenderFile, AddSourceFilter, FilterCollection, RegFilterCollection, StopWhenReady: not used.
    }

    [ComImport, Guid("56a868b6-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    internal interface IMediaEvent
    {
        [PreserveSig] int GetEventHandle(out IntPtr hEvent);
        [PreserveSig] int GetEvent(out int lEventCode, out IntPtr lParam1, out IntPtr lParam2, int msTimeout);
        [PreserveSig] int WaitForCompletion(int msTimeout, out int pEvCode);
        [PreserveSig] int CancelDefaultHandling(int evCode);
        [PreserveSig] int RestoreDefaultHandling(int evCode);
        [PreserveSig] int FreeEventParams(int evCode, IntPtr lParam1, IntPtr lParam2);
    }

    // ------------------------------------------------------------------ sample grabber

    [ComImport, Guid("0579154a-2b53-4994-b0d0-e773148eff85"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ISampleGrabberCB
    {
        [PreserveSig] int SampleCB(double sampleTime, IntPtr pSample);
        [PreserveSig] int BufferCB(double sampleTime, IntPtr pBuffer, int bufferLen);
    }

    [ComImport, Guid("6b652fff-11fe-4fce-92ad-0266b5d7c78f"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ISampleGrabber
    {
        [PreserveSig] int SetOneShot([In, MarshalAs(UnmanagedType.Bool)] bool oneShot);
        [PreserveSig] int SetMediaType(IntPtr pmt);
        [PreserveSig] int GetConnectedMediaType(IntPtr pmt);
        [PreserveSig] int SetBufferSamples([In, MarshalAs(UnmanagedType.Bool)] bool bufferThem);
        [PreserveSig] int GetCurrentBuffer(ref int pBufferSize, IntPtr pBuffer);
        [PreserveSig] int GetCurrentSample(out IntPtr ppSample);
        [PreserveSig] int SetCallback([In, MarshalAs(UnmanagedType.Interface)] ISampleGrabberCB pCallback, int whichMethodToCallback);
    }

    // ------------------------------------------------------------------ capture configuration

    [ComImport, Guid("c6e13340-30ac-11d0-a18c-00a0c9118956"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAMStreamConfig
    {
        [PreserveSig] int SetFormat(IntPtr pmt);
        [PreserveSig] int GetFormat(out IntPtr ppmt);
        [PreserveSig] int GetNumberOfCapabilities(out int piCount, out int piSize);
        [PreserveSig] int GetStreamCaps(int iIndex, out IntPtr ppmt, IntPtr pSCC);
    }

    [ComImport, Guid("56ed71a0-af5f-11d0-b3f0-00aa003761c5"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAMBufferNegotiation
    {
        [PreserveSig] int SuggestAllocatorProperties(ref AllocatorProperties pprop);
        [PreserveSig] int GetAllocatorProperties(out AllocatorProperties pprop);
    }

    [ComImport, Guid("c6e13380-30ac-11d0-a18c-00a0c9118956"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAMCrossbar
    {
        [PreserveSig] int get_PinCounts(out int outputPinCount, out int inputPinCount);
        [PreserveSig] int CanRoute(int outputPinIndex, int inputPinIndex);
        [PreserveSig] int Route(int outputPinIndex, int inputPinIndex);
        [PreserveSig] int get_IsRoutedTo(int outputPinIndex, out int inputPinIndex);
        [PreserveSig] int get_CrossbarPinInfo([MarshalAs(UnmanagedType.Bool)] bool isInputPin, int pinIndex, out int pinIndexRelated, out int physicalType);
    }

    // ------------------------------------------------------------------ device enumeration

    [ComImport, Guid("29840822-5b84-11d0-bd3b-00a0c911ce86"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ICreateDevEnum
    {
        [PreserveSig] int CreateClassEnumerator([In] ref Guid pType, out IEnumMoniker ppEnumMoniker, [In] int dwFlags);
    }

    [ComImport, Guid("55272a00-42cb-11ce-8135-00aa004bb851"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPropertyBag
    {
        [PreserveSig] int Read([In, MarshalAs(UnmanagedType.LPWStr)] string pszPropName, [Out, MarshalAs(UnmanagedType.Struct)] out object pVar, IntPtr pErrorLog);
        [PreserveSig] int Write([In, MarshalAs(UnmanagedType.LPWStr)] string pszPropName, [In, MarshalAs(UnmanagedType.Struct)] ref object pVar);
    }
}
