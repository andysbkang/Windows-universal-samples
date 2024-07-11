//*********************************************************
//
// Copyright (c) Microsoft. All rights reserved.
// This code is licensed under the MIT License (MIT).
// THIS CODE IS PROVIDED *AS IS* WITHOUT WARRANTY OF
// ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING ANY
// IMPLIED WARRANTIES OF FITNESS FOR A PARTICULAR
// PURPOSE, MERCHANTABILITY, OR NON-INFRINGEMENT.
//
//*********************************************************

using SDKTemplate;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Windows.Foundation;
using Windows.Media;
using Windows.Media.Audio;
using Windows.Media.MediaProperties;
using Windows.Media.Render;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=234238

namespace AudioCreation
{
    // We are initializing a COM interface for use within the namespace
    // This interface allows access to memory at the byte level which we need to populate audio data that is generated
    [ComImport]
    [Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]

    unsafe interface IMemoryBufferByteAccess
    {
        void GetBuffer(out byte* buffer, out uint capacity);
    }

    /// <summary>
    /// Scenario 3: Using a frame input node to play audio. Audio data is dynamically generated to populate the audio frame
    /// </summary>
    public sealed partial class Scenario3_FrameInput : Page
    {
        private MainPage rootPage;
        private AudioGraph graph;
        private AudioDeviceOutputNode deviceOutputNodeA;
        private AudioDeviceOutputNode deviceOutputNodeB;
        private List<AudioFrameInputNode> frameInputNodes;
        private uint streamCount = 1;
        private uint channelCount = 2;
        public double theta = 0;

        public Scenario3_FrameInput()
        {
            this.InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            rootPage = MainPage.Current;
            await CreateAudioGraph(channelCount);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            if (graph != null)
            {
                graph.Dispose();
            }
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            if (generateButton.Content.Equals("Generate Audio"))
            {
                foreach (AudioFrameInputNode node in frameInputNodes)
                {
                    node.Start();
                }

                generateButton.Content = "Stop";
                audioPipe.Fill = new SolidColorBrush(Colors.Blue);
            }
            else if (generateButton.Content.Equals("Stop"))
            {
                foreach (AudioFrameInputNode node in frameInputNodes)
                {
                    node.Stop();
                }

                generateButton.Content = "Generate Audio";
                audioPipe.Fill = new SolidColorBrush(Color.FromArgb(255, 49, 49, 49));
            }
        }

        unsafe private static AudioFrame GenerateAudioData(uint channelCount, uint samples, int sampleRate)
        {
            // Buffer size is (number of samples) * (size of each sample)
            // We choose to generate single channel (mono) audio. For multi-channel, multiply by number of channels
            uint bufferSize = samples * sizeof(float) * channelCount;
            AudioFrame frame = new Windows.Media.AudioFrame(bufferSize);

            using (AudioBuffer buffer = frame.LockBuffer(AudioBufferAccessMode.Write))
            using (IMemoryBufferReference reference = buffer.CreateReference())
            {
                byte* dataInBytes;
                uint capacityInBytes;
                float* dataInFloat;

                // Get the buffer from the AudioFrame
                ((IMemoryBufferByteAccess)reference).GetBuffer(out dataInBytes, out capacityInBytes);

                // Cast to float since the data we are generating is float
                dataInFloat = (float*)dataInBytes;

                float freq = samples; // choosing to generate frequency equal to sample size
                float amplitude = 0.3f;
                double sampleIncrement = (Math.PI * 2) / samples;

                for (int i = 0; i < samples; i++)
                {
                    double sinValue = amplitude * Math.Sin(i * sampleIncrement);
                    dataInFloat[i] = (float)sinValue;
                }
            }

            return frame;
        }
           
        private async Task CreateAudioGraph(uint streamCount)
        {
            if (graph != null)
            {
                graph.Stop();
                graph.ResetAllNodes();
            }

            // Create an AudioGraph with default settings
            AudioGraphSettings settings = new AudioGraphSettings(AudioRenderCategory.Media);
            CreateAudioGraphResult result = await AudioGraph.CreateAsync(settings);

            if (result.Status != AudioGraphCreationStatus.Success)
            {
                // Cannot create graph
                rootPage.NotifyUser(String.Format("AudioGraph Creation Error because {0}", result.Status.ToString()), NotifyType.ErrorMessage);
                return;
            }

            graph = result.Graph;

            // Create a device output node
            CreateAudioDeviceOutputNodeResult deviceOutputNodeResultA = await graph.CreateDeviceOutputNodeAsync();
            if (deviceOutputNodeResultA.Status != AudioDeviceNodeCreationStatus.Success)
            {
                // Cannot create device output node
                rootPage.NotifyUser(String.Format("Audio Device Output unavailable because {0}", deviceOutputNodeResultA.Status.ToString()), NotifyType.ErrorMessage);
                speakerContainer.Background = new SolidColorBrush(Colors.Red);
            }

            deviceOutputNodeA = deviceOutputNodeResultA.DeviceOutputNode;

            //await Task.Delay(2000);

            //CreateAudioDeviceOutputNodeResult deviceOutputNodeResultB = await graph.CreateDeviceOutputNodeAsync();
            //if (deviceOutputNodeResultB.Status != AudioDeviceNodeCreationStatus.Success)
            //{
            //    // Cannot create device output node
            //    rootPage.NotifyUser(String.Format("Audio Device Output unavailable because {0}", deviceOutputNodeResultB.Status.ToString()), NotifyType.ErrorMessage);
            //    speakerContainer.Background = new SolidColorBrush(Colors.Red);
            //}

            //deviceOutputNodeB = deviceOutputNodeResultB.DeviceOutputNode;

            rootPage.NotifyUser("Device Output Node successfully created", NotifyType.StatusMessage);
            speakerContainer.Background = new SolidColorBrush(Colors.Green);

            // Create the FrameInputNode at the same format as the graph, except explicitly set mono.
            AudioEncodingProperties nodeEncodingProperties = graph.EncodingProperties;
            nodeEncodingProperties.ChannelCount = channelCount;

            frameInputNodes = new List<AudioFrameInputNode>();

            for (uint i = 0; i < streamCount; ++i)
            {
                AudioFrameInputNode frameInputNode = graph.CreateFrameInputNode(nodeEncodingProperties);
                frameInputNode.AddOutgoingConnection(deviceOutputNodeA);
                frameContainer.Background = new SolidColorBrush(Colors.Green);

                // Initialize the Frame Input Node in the stopped state
                frameInputNode.Stop();

                // Hook up an event handler so we can start generating samples when needed
                // This event is triggered when the node is required to provide data
                frameInputNode.QuantumStarted += node_QuantumStarted;

                frameInputNodes.Add(frameInputNode);
            }
            
            // Start the graph since we will only start/stop the frame input node
            graph.Start();
        }

        private void node_QuantumStarted(AudioFrameInputNode sender, FrameInputNodeQuantumStartedEventArgs args)
        {
            // GenerateAudioData can provide PCM audio data by directly synthesizing it or reading from a file.
            // Need to know how many samples are required. In this case, the node is running at the same rate as the rest of the graph
            // For minimum latency, only provide the required amount of samples. Extra samples will introduce additional latency.
            uint numSamplesNeeded = (uint) args.RequiredSamples;
            //uint channelCount = 2;

            //var dispatcher = Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher;
            //var task = dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            //{
            //    // Update the TextBox here
            //    channelCount = uint.Parse(channelCountText.Text);
            //}).AsTask();

            //// Wait for the task to complete
            //task.Wait();

            if (numSamplesNeeded != 0)
            {
                AudioFrame audioData = GenerateAudioData(channelCount, numSamplesNeeded, (int)graph.EncodingProperties.SampleRate);
                sender.AddFrame(audioData);
            }
        }

        private void createNodesButton_Click(object sender, RoutedEventArgs e)
        {
            streamCount = uint.Parse(streamCountText.Text);

            CreateAudioGraph(streamCount);
        }

        private void channelCountText_TextChanged(object sender, TextChangedEventArgs e)
        {
            channelCount = uint.Parse(((TextBox)sender).Text);
        }
    }
}
