using System.Threading;

internal static class AdbHandlerExtensions
{
    public static void UnlockDeviceAndSwipeUp(this AdbHandler adbHandler)
    {
        adbHandler.WakeDevice();
        Thread.Sleep(250);

        adbHandler.SwipeUp();
        Thread.Sleep(250);
    }

    public static void WakeDevice(this AdbHandler adbHandler)
    {
        adbHandler.TurnScreenOn();
    }

    public static void TurnScreenOn(this AdbHandler adbHandler)
    {
        adbHandler.RunCommand("shell input keyevent 224", throwOnFailure: false);
    }

    public static void TurnScreenOff(this AdbHandler adbHandler)
    {
        adbHandler.RunCommand("shell input keyevent 223", throwOnFailure: false);
    }

    public static void SwipeUp(this AdbHandler adbHandler)
    {
        adbHandler.RunCommand("shell input swipe 500 1600 500 500", throwOnFailure: false);
    }
}
