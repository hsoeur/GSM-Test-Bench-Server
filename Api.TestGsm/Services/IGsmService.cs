namespace Api.TestGsm.Services;

public interface IGsmService
{
    void TestBenchInitialization();
    void FourGModuleInitialization();
    void LaunchTest(string phoneNumber, bool audioOk);
    void HangUpCall();

    // Health
    bool IsPortOpen();


    bool PingModem();
}
