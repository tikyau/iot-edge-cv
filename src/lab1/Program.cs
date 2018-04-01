#define IOT_EDGE

namespace CameraModule2
{
    using System;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Runtime.Loader;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Devices.Client;
    using Microsoft.Azure.Devices.Client.Transport.Mqtt;
   
    using Unosquare.RaspberryIO;
    using Unosquare.RaspberryIO.Camera;
    using Unosquare.RaspberryIO.Computer;
    using Unosquare.RaspberryIO.Gpio;
   
    using Microsoft.Azure.Devices.Shared;
    using Newtonsoft.Json;
    using System.Collections.Generic;

    class Program
    {
        class Test{
            public string TestText  = "";
        }
        static int counter;
        static List<Task> tasks = new List<Task>();
        static bool running = true;
        private async static void CaptureImage(DeviceClient iotHubModuleClient)
        {
            try{
                counter ++;
                Console.WriteLine("Opening Camera...");

                var pictureBytes = Pi.Camera.CaptureImageJpeg(640,480);
                //var pictureBytes = Encoding.UTF8.GetBytes("{'msg':'ok'}");
                Console.WriteLine("Here...");
                var msg = new Message(pictureBytes);
#if IOT_EDGE
                //await iotHubModuleClient.SendEventAsync("cameraOut", msg);
#else
                await iotHubModuleClient.SendEventAsync(msg);
#endif
            }
            catch(Exception exp){
                Console.WriteLine($"Exception:{exp.Message}");
                Console.WriteLine($"==> :{exp.StackTrace}");
                if(exp.InnerException != null){
                    Console.WriteLine($"InnerException:{exp.Message}");
                    Console.WriteLine($"==> :{exp.StackTrace}");
                }
            }

        }
        static void Main(string[] args)
        {
            var osNameAndVersion = System.Runtime.InteropServices.RuntimeInformation.OSDescription;
            Console.WriteLine(osNameAndVersion);
            // The Edge runtime gives us the connection string we need -- it is injected as an environment variable
            string connectionString = Environment.GetEnvironmentVariable("EdgeHubConnectionString");
            //connectionString = "HostName=opc-iot-hub.azure-devices.net;DeviceId=rpiedge01;SharedAccessKey=99COPKwf1Vw4YgX/YJCiryepg+FKbVzKaOxX0vcme08=";
            bool bypassCertVerification = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
#if IOT_EDGE
            // Cert verification is not yet fully functional when using Windows OS for the container
            if (!bypassCertVerification) InstallCert();
#endif
            Init(connectionString, bypassCertVerification).Wait();

            // Wait until the app unloads or is cancelled
            var cts = new CancellationTokenSource();
            AssemblyLoadContext.Default.Unloading += (ctx) => cts.Cancel();
            Console.CancelKeyPress += (sender, cpe) => cts.Cancel();

            
            WhenCancelled(cts.Token).Wait();
        }

        /// <summary>
        /// Handles cleanup operations when app is cancelled or unloads
        /// </summary>
        public static Task WhenCancelled(CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>();
            cancellationToken.Register(s => ((TaskCompletionSource<bool>)s).SetResult(true), tcs);
            return tcs.Task;
        }

        /// <summary>
        /// Add certificate in local cert store for use by client for secure connection to IoT Edge runtime
        /// </summary>
        static void InstallCert()
        {
            string certPath = Environment.GetEnvironmentVariable("EdgeModuleCACertificateFile");
            if (string.IsNullOrWhiteSpace(certPath))
            {
                // We cannot proceed further without a proper cert file
                Console.WriteLine($"Missing path to certificate collection file: {certPath}");
                throw new InvalidOperationException("Missing path to certificate file.");
            }
            else if (!File.Exists(certPath))
            {
                // We cannot proceed further without a proper cert file
                Console.WriteLine($"Missing path to certificate collection file: {certPath}");
                throw new InvalidOperationException("Missing certificate file.");
            }
            X509Store store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            store.Add(new X509Certificate2(X509Certificate2.CreateFromCertFile(certPath)));
            Console.WriteLine("Added Cert: " + certPath);
            store.Close();
        }

        /// <summary>
        /// Initializes the DeviceClient and sets up the callback to receive
        /// messages containing temperature information
        /// </summary>
        static async Task Init(string connectionString, bool bypassCertVerification = false)
        {
            Console.WriteLine("Connection String {0}", connectionString);

            MqttTransportSettings mqttSetting = new MqttTransportSettings(TransportType.Mqtt_Tcp_Only);
            // During dev you might want to bypass the cert verification. It is highly recommended to verify certs systematically in production
            if (bypassCertVerification)
            {
                mqttSetting.RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;
            }
            ITransportSettings[] settings = { mqttSetting };
            DeviceClient iotHubModuleClient = DeviceClient.CreateFromConnectionString(connectionString, settings);
            await iotHubModuleClient.OpenAsync();
            Console.WriteLine("IoT Hub Module Client Opened...");
            Twin iotModuleTwin = await iotHubModuleClient.GetTwinAsync();

            await UpdateFromTwin(iotModuleTwin.Properties.Desired, iotHubModuleClient);
            await iotHubModuleClient.SetDesiredPropertyUpdateCallbackAsync(
                OnDesiredPropertiesUpdate,
                iotHubModuleClient);
        }

        private static async Task OnDesiredPropertiesUpdate(TwinCollection desiredProperties, object userContext)
        {
            DeviceClient ioTHubModuleClient = userContext as DeviceClient;
            running = false;
            try
            {
#if IOT_EDGE
                // stop all activities while updating configuration
                await ioTHubModuleClient.SetInputMessageHandlerAsync(
                    "input",
                    DummyCallBack,
                    null);
#endif
                await UpdateFromTwin(desiredProperties, ioTHubModuleClient);
            }
            catch
            {

            }

        }

        private static async Task<MessageResponse> DummyCallBack(Message message, object userContext)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(0));
            return MessageResponse.Abandoned;
        }

        private static async Task UpdateFromTwin(TwinCollection desired, DeviceClient iotHubModuleClient)
        {
            if (desired != null)
            {
                foreach (var property in desired)
                {

                }
            }
#if IOT_EDGE
            await iotHubModuleClient.SetInputMessageHandlerAsync(
                "input",
                PipeMessage,
                iotHubModuleClient
                );
#else
            await iotHubModuleClient.GetTwinAsync();
#endif
            tasks.Add(Start(iotHubModuleClient));
        }
        static async Task Start(DeviceClient iotHubModuleClient)
        {
            running = true;
            while (running){
                CaptureImage(iotHubModuleClient);
                await Task.Delay(1000 * 3);
            }
        }
        /// <summary>
        /// This method is called whenever the module is sent a message from the EdgeHub. 
        /// It just pipe the messages without any change.
        /// It prints all the incoming messages.
        /// </summary>
        static async Task<MessageResponse> PipeMessage(Message message, object userContext)
        {
            //DO NOTHING for now
            var deviceClient = userContext as DeviceClient;
            await deviceClient.CompleteAsync(message);
            return MessageResponse.Completed;
#if false
            int counterValue = Interlocked.Increment(ref counter);

            var deviceClient = userContext as DeviceClient;
            if (deviceClient == null)
            {
                throw new InvalidOperationException("UserContext doesn't contain " + "expected values");
            }

            byte[] messageBytes = message.GetBytes();
            string messageString = Encoding.UTF8.GetString(messageBytes);
            Console.WriteLine($"Received message: {counterValue}, Body: [{messageString}]");

            if (!string.IsNullOrEmpty(messageString))
            {
                var pipeMessage = new Message(messageBytes);
                foreach (var prop in message.Properties)
                {
                    pipeMessage.Properties.Add(prop.Key, prop.Value);
                }
                await deviceClient.SendEventAsync("cameraOutput", pipeMessage);
                Console.WriteLine("Received message sent");
            }

            return MessageResponse.Completed;
#endif
        }
    }
}

/*
Added Cert: /mnt/edgemodule/edge-device-ca.cert.pem
Connection String HostName=opc-iot-hub.azure-devices.net;GatewayHostName=raspberrypi;DeviceId=iotedge002;ModuleId=camera;SharedAccessKey=tSxECQfVV2g5EhXcAUI17IHo8/wZyjpdtusXzrgp8AI=
IoT Hub Module Client Opened...
Opening Camera...

Unhandled Exception: System.ComponentModel.Win32Exception: No such file or directory
   at System.Diagnostics.Process.ResolvePath(String filename)
   at System.Diagnostics.Process.StartCore(ProcessStartInfo startInfo)
   at System.Diagnostics.Process.Start()
   at Unosquare.Swan.Components.ProcessRunner.<>c__DisplayClass4_0.<RunProcessAsync>b__0()
   at System.Threading.Tasks.Task`1.InnerInvoke()
   at System.Threading.Tasks.Task.<>c.<.cctor>b__276_1(Object obj)
   at System.Threading.ExecutionContext.Run(ExecutionContext executionContext, ContextCallback callback, Object state)
   at System.Threading.Tasks.Task.ExecuteWithThreadLocal(Task& currentTaskSlot)
--- End of stack trace from previous location where exception was thrown ---
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task)
   at Unosquare.RaspberryIO.Camera.CameraController.<CaptureImageAsync>d__5.MoveNext()
--- End of stack trace from previous location where exception was thrown ---
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task)
   at Unosquare.RaspberryIO.Camera.CameraController.CaptureImageJpeg(Int32 width, Int32 height)
   at CameraModule2.Program.<CaptureImage>d__4.MoveNext() in /app/Program.cs:line 36
--- End of stack trace from previous location where exception was thrown ---
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.AsyncMethodBuilderCore.<>c.<ThrowAsync>b__6_1(Object state)
   at System.Threading.QueueUserWorkItemCallbackDefaultContext.WaitCallback_Context(Object state)
   at System.Threading.ExecutionContext.Run(ExecutionContext executionContext, ContextCallback callback, Object state)
   at System.Threading.QueueUserWorkItemCallbackDefaultContext.System.Threading.IThreadPoolWorkItem.ExecuteWorkItem()
   at System.Threading.ThreadPoolWorkQueue.Dispatch()
   at System.Threading._ThreadPoolWaitCallback.PerformWaitCallback()
*/