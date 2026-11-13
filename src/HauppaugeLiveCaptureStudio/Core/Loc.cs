using System;
using System.Collections.Generic;
using System.Globalization;

namespace HauppaugeLiveCaptureStudio.Core
{
    /// <summary>English / Arabic texts. Arabic switches the whole window to right-to-left.</summary>
    internal static class Loc
    {
        private static readonly Dictionary<string, string> En = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> Ar = new Dictionary<string, string>();
        private static string _lang = "en";

        public static bool IsArabic { get { return _lang == "ar"; } }
        public static string Language { get { return _lang; } }

        public static void SetLanguage(string lang)
        {
            _lang = string.Equals(lang, "ar", StringComparison.OrdinalIgnoreCase) ? "ar" : "en";
        }

        public static string T(string key)
        {
            string v;
            if (_lang == "ar" && Ar.TryGetValue(key, out v)) return v;
            if (En.TryGetValue(key, out v)) return v;
            return key;
        }

        public static string T(string key, params object[] args)
        {
            try { return string.Format(CultureInfo.CurrentCulture, T(key), args); }
            catch (FormatException) { return T(key); }
        }

        private static void A(string key, string en, string ar)
        {
            En[key] = en;
            Ar[key] = ar;
        }

        static Loc()
        {
            A("app.title", "Hauppauge Live Capture Studio", "Hauppauge Live Capture Studio");
            A("lang.button", "العربية", "English");

            // status light
            A("led.notfound", "Device not connected", "الجهاز غير متصل");
            A("led.ready", "Connected — {0}", "متصل — {0}");
            A("led.live", "LIVE — {0}", "بث مباشر — {0}");
            A("led.stalled", "Connected, but no video frames are arriving", "الجهاز متصل لكن لا تصل إطارات فيديو");
            A("led.lost", "Device disconnected", "تم فصل الجهاز");

            // sections and labels
            A("sec.device", "CAPTURE DEVICE", "جهاز الالتقاط");
            A("sec.video", "VIDEO SETTINGS", "إعدادات الفيديو");
            A("sec.live", "LIVE VIEW", "البث المباشر");
            A("sec.snapshot", "SNAPSHOT", "التقاط الصور");
            A("sec.record", "RECORDING (video only, no audio)", "تسجيل الفيديو (صورة فقط بدون صوت)");
            A("lbl.source", "Video source", "مصدر الفيديو");
            A("lbl.resolution", "Resolution", "الدقة");
            A("lbl.fps", "Frame rate (fps)", "معدل الإطارات (إطار/ثانية)");
            A("lbl.aspect", "Picture shape", "شكل الصورة");
            A("lbl.snapName", "Picture name", "اسم الصورة");
            A("lbl.snapFormat", "Format", "الصيغة");
            A("lbl.vidName", "Video name", "اسم الفيديو");
            A("chk.appendDate", "Add date and time to the file name", "إضافة التاريخ والوقت إلى اسم الملف");
            A("aspect.auto", "Auto (4:3 for SD video)", "تلقائي (4:3 لفيديو SD)");
            A("aspect.native", "Square pixels (as captured)", "بكسلات مربعة (كما التُقطت)");
            A("aspect.wide", "16:9", "16:9");
            A("aspect.stretch", "Fill the window", "ملء النافذة");
            A("src.composite", "Composite", "كومبوزيت (Composite)");
            A("src.svideo", "S-Video", "إس-فيديو (S-Video)");
            A("src.none", "No selectable inputs", "لا توجد مصادر قابلة للاختيار");
            A("dev.none", "No capture device found", "لم يتم العثور على جهاز التقاط");
            A("res.none", "Driver default", "الإعداد الافتراضي للتعريف");

            // buttons
            A("btn.startLive", "START LIVE", "بدء البث");
            A("btn.stopLive", "STOP LIVE", "إيقاف البث");
            A("btn.snapshot", "SNAPSHOT", "التقاط صورة");
            A("btn.startRec", "START RECORDING", "بدء التسجيل");
            A("btn.stopRec", "STOP RECORDING", "إيقاف التسجيل");
            A("btn.full", "FULL SCREEN", "ملء الشاشة");
            A("btn.exitFull", "EXIT FULL SCREEN", "إنهاء ملء الشاشة");
            A("btn.settings", "Settings", "الإعدادات");
            A("btn.refresh", "Refresh", "تحديث");

            // video area / info line
            A("view.placeholder", "Press START LIVE to see the video", "اضغط بدء البث لعرض الفيديو");
            A("view.placeholderNoDevice", "Connect the USB-Live2 and install its driver", "وصّل جهاز USB-Live2 وثبّت تعريفه");
            A("info.stats", "FPS {0}  |  dropped {1}  |  display delay ≈ {2} ms", "المعدل {0}  |  مفقود {1}  |  تأخير العرض ≈ {2} م.ث");
            A("ffmpeg.missing", "ffmpeg.exe not found — recording is unavailable (see Settings)", "ffmpeg.exe غير موجود — التسجيل غير متاح (انظر الإعدادات)");
            A("ffmpeg.ok", "FFmpeg ready", "FFmpeg جاهز");

            // messages
            A("msg.ready", "Ready", "جاهز");
            A("msg.reading", "Reading device capabilities...", "جارٍ قراءة إمكانيات الجهاز...");
            A("msg.starting", "Starting live view...", "جارٍ بدء البث المباشر...");
            A("msg.stopping", "Stopping...", "جارٍ الإيقاف...");
            A("msg.liveStarted", "Live view started", "بدأ البث المباشر");
            A("msg.liveStopped", "Live view stopped", "توقف البث المباشر");
            A("msg.noDevice", "No capture device is connected.", "لا يوجد جهاز التقاط متصل.");
            A("msg.startFailed", "Could not start the live view.", "تعذر بدء البث المباشر.");
            A("msg.noLive", "Start the live view first.", "ابدأ البث المباشر أولاً.");
            A("msg.snapshotSaved", "Picture saved: {0}", "تم حفظ الصورة: {0}");
            A("msg.snapshotFailed", "Could not save the picture.", "تعذر حفظ الصورة.");
            A("msg.recStarted", "Recording started: {0}", "بدأ التسجيل: {0}");
            A("msg.recSaved", "Video saved: {0}", "تم حفظ الفيديو: {0}");
            A("msg.recFailed", "Recording failed.", "فشل التسجيل.");
            A("msg.noFfmpeg", "ffmpeg.exe was not found. Put it next to the program (ffmpeg\\ffmpeg.exe) or choose it in Settings.", "لم يتم العثور على ffmpeg.exe. ضعه بجانب البرنامج (ffmpeg\\ffmpeg.exe) أو اخترْه من الإعدادات.");
            A("msg.lockedRec", "Stop the recording before changing the device or the video settings.", "أوقف التسجيل قبل تغيير الجهاز أو إعدادات الفيديو.");
            A("msg.deviceLost", "The USB capture device was disconnected or stopped responding.", "تم فصل جهاز الالتقاط USB أو توقف عن الاستجابة.");
            A("msg.deviceLostRec", "The recording was stopped and the file was saved:\n{0}", "تم إيقاف التسجيل وحفظ الملف:\n{0}");
            A("msg.deviceLostHint", "Reconnect the device, press Refresh, then START LIVE.", "أعد توصيل الجهاز ثم اضغط تحديث ثم بدء البث.");
            A("msg.stalled", "No video frames are arriving. Check the cable, the video source and the driver.", "لا تصل إطارات فيديو. تحقق من الكابل ومصدر الفيديو والتعريف.");
            A("msg.resumed", "Video is back.", "عاد الفيديو.");
            A("msg.quitRecording", "A recording is running. Stop it, save the file and exit?", "هناك تسجيل جارٍ. هل تريد إيقافه وحفظ الملف ثم الخروج؟");
            A("msg.alreadyRunning", "Hauppauge Live Capture Studio is already running.", "البرنامج قيد التشغيل بالفعل.");
            A("msg.rootChanged", "Save folder changed to:\n{0}", "تم تغيير مجلد الحفظ إلى:\n{0}");
            A("msg.rootFailed", "The folder could not be used:\n{0}", "تعذر استخدام المجلد:\n{0}");
            A("msg.diagCopied", "Diagnostics copied to the clipboard.", "تم نسخ معلومات التشخيص إلى الحافظة.");

            // settings window
            A("set.title", "Settings", "الإعدادات");
            A("set.language", "Language", "اللغة");
            A("set.folder", "Save folder (Pictures, Videos and Settings are created inside it)", "مجلد الحفظ (تُنشأ بداخله المجلدات Pictures وVideos وSettings)");
            A("set.browse", "Browse...", "استعراض...");
            A("set.ffmpeg", "FFmpeg program (ffmpeg.exe)", "برنامج FFmpeg (ffmpeg.exe)");
            A("set.ffmpegAuto", "(automatic)", "(تلقائي)");
            A("set.ffmpegFound", "Found: {0}", "تم العثور عليه: {0}");
            A("set.ffmpegMissing", "Not found", "غير موجود");
            A("set.quality", "Recording quality", "جودة التسجيل");
            A("set.q.high", "High (largest files)", "عالية (ملفات أكبر)");
            A("set.q.std", "Standard", "قياسية");
            A("set.q.small", "Compact (smaller files)", "مضغوطة (ملفات أصغر)");
            A("set.fragmented", "Crash-safe MP4 (the file stays playable after a power loss)", "MP4 آمن (يبقى الملف قابلاً للتشغيل بعد انقطاع الكهرباء)");
            A("set.autostart", "Start the live view automatically when the program opens", "بدء البث تلقائياً عند فتح البرنامج");
            A("set.diag", "Copy diagnostics", "نسخ معلومات التشخيص");
            A("set.openFolder", "Open save folder", "فتح مجلد الحفظ");
            A("set.ok", "OK", "موافق");
            A("set.cancel", "Cancel", "إلغاء");
            A("dlg.pickFolder", "Choose the save folder", "اختر مجلد الحفظ");
            A("dlg.pickFfmpeg", "Choose ffmpeg.exe", "اختر ffmpeg.exe");
        }
    }
}
