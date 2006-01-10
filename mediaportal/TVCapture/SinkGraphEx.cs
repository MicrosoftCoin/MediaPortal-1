/* 
 *	Copyright (C) 2005 Team MediaPortal
 *	http://www.team-mediaportal.com
 *
 *  This Program is free software; you can redistribute it and/or modify
 *  it under the terms of the GNU General Public License as published by
 *  the Free Software Foundation; either version 2, or (at your option)
 *  any later version.
 *   
 *  This Program is distributed in the hope that it will be useful,
 *  but WITHOUT ANY WARRANTY; without even the implied warranty of
 *  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 *  GNU General Public License for more details.
 *   
 *  You should have received a copy of the GNU General Public License
 *  along with GNU Make; see the file COPYING.  If not, write to
 *  the Free Software Foundation, 675 Mass Ave, Cambridge, MA 02139, USA. 
 *  http://www.gnu.org/copyleft/gpl.html
 *
 */
#if (UseCaptureCardDefinitions)
/*
 * This is a modified version of the SinkGraph class.
 * It supports MPEG2 hardware cards using a definition file.
 * 
 */

using System;
using System.Collections;
using System.Drawing;
using System.Runtime.InteropServices;
using DShowNET;
using DShowNET.Helper;
using DirectShowLib;
using MediaPortal.Util;
using MediaPortal.Player;
using MediaPortal.GUI.Library;
using Microsoft.Win32;
using System.Xml;
using TVCapture;
//using DirectX.Capture;
using MediaPortal.TV.Teletext;


namespace MediaPortal.TV.Recording
{
  /// <summary>
  /// Implementation of IGraph for all hardware MPEG2 encoder cards (pretentions... ;-) )
  /// As there are the Hauppauge PVR 150MCE, 250, 350 and the WinFast PVR 2000
  /// 
  /// The graphbuilder can build the graph for the following functions:
  /// -tv viewing
  /// -tv recording
  /// -tv timeshifting
  /// </summary>	
  public class SinkGraphEx : MediaPortal.TV.Recording.SinkGraph, ISampleGrabberCB
  {
    #region imports
    [DllImport("dshowhelper.dll", ExactSpelling = true, CharSet = CharSet.Auto, SetLastError = true)]
    unsafe private static extern bool AddTeeSinkToGraph(IGraphBuilder graph);
    [DllImport("dshowhelper.dll", ExactSpelling = true, CharSet = CharSet.Auto, SetLastError = true)]
    unsafe private static extern void AddWstCodecToGraph(IGraphBuilder graph);
    #endregion

    IBaseFilter m_sampleGrabber = null;
    ISampleGrabber m_sampleInterface = null;

    #region Constructors

    /// <summary>
    /// Constructor for the graph  for given capture card.
    /// </summary>
    /// <param name="pCard"></param>
    public SinkGraphEx(TVCaptureDevice pCard)
      : base(pCard)
    {
    }
    #endregion

    #region Overrides

    /// <summary>
    /// Creates a new DirectShow graph for the TV capturecard
    /// </summary>
    /// <returns>bool indicating if graph is created or not</returns>
    public override bool CreateGraph(int Quality)
    {
      try
      {
        _hasTeletext = false;
        _grabTeletext = false;
        Vmr9 = new VMR9Util("mytv");
        Vmr7 = new VMR7Util();

        Log.WriteFile(Log.LogType.Capture, "SinkGraphEx:CreateGraph() IN");
        if (m_graphState != State.None) return false;		// If doing something already, return...
        if (mCard == null)
        {
          Log.WriteFile(Log.LogType.Capture, true, "SinkGraphEx:card is not defined");
          return false;
        }

        if (!mCard.LoadDefinitions())											// Load configuration for this card
        {
          Log.WriteFile(Log.LogType.Capture, "SinkGraphEx: Loading card definitions for card {0} failed", mCard.Graph.CommercialName);
          return false;
        }
        if (mCard.Graph.TvFilterDefinitions == null)
        {
          Log.WriteFile(Log.LogType.Capture, true, "SinkGraphEx:card does not contain filters?");
          return false;
        }
        if (mCard.Graph.TvConnectionDefinitions == null)
        {
          Log.WriteFile(Log.LogType.Capture, true, "SinkGraphEx:card does not contain connections for tv?");
          return false;
        }

        GUIGraphicsContext.OnGammaContrastBrightnessChanged += new VideoGammaContrastBrightnessHandler(OnGammaContrastBrightnessChanged);


        // Initialize settings. No channel tuned yet...
        m_iPrevChannel = -1;

        int hr = 0;

        // Make a new filter graph
        Log.WriteFile(Log.LogType.Capture, "SinkGraphEx: Create new filter graph (IGraphBuilder)");
        m_graphBuilder = (IGraphBuilder)new FilterGraph();

        // Get the Capture Graph Builder...
        Log.WriteFile(Log.LogType.Capture, "SinkGraphEx: Get the Capture Graph Builder (ICaptureGraphBuilder2)");
        m_captureGraphBuilder = (ICaptureGraphBuilder2)new CaptureGraphBuilder2();

        // ...and link the Capture Graph Builder to the Graph Builder
        Log.WriteFile(Log.LogType.Capture, "SinkGraphEx: Link the CaptureGraphBuilder to the filter graph (SetFiltergraph)");
        hr = m_captureGraphBuilder.SetFiltergraph(m_graphBuilder);
        if (hr < 0)
        {
          Log.WriteFile(Log.LogType.Capture, "SinkGraphEx: Error: link FAILED");
          Log.WriteFile(Log.LogType.Capture, "SinkGraphEx:CreateGraph() OUT");
          return false;
        }
        // Add graph to Running Object Table (ROT), so we can connect to the graph using GraphEdit ;)
        Log.WriteFile(Log.LogType.Capture, "SinkGraphEx: Add graph to ROT table");
        _rotEntry = new DsROTEntry((IFilterGraph)m_graphBuilder);

        // Loop through configured filters for this card, bind them and add them to the graph
        // Note that while adding filters to a graph, some connections may already be created...
        Log.WriteFile(Log.LogType.Capture, "SinkGraphEx: Adding configured filters...");
        foreach (FilterDefinition dsFilter in mCard.Graph.TvFilterDefinitions)
        {
          string catName = dsFilter.Category;
          dsFilter.DSFilter = GetFilter(dsFilter.FriendlyName);
          if (dsFilter.DSFilter == null)
          {
            dsFilter.DSFilter = Marshal.BindToMoniker(dsFilter.MonikerDisplayName) as IBaseFilter;
          }
          //FilterDefinition dsFilter = mCard.TvFilterDefinitions[catName] as FilterDefinition;
          Log.WriteFile(Log.LogType.Capture, "SinkGraphEx:  adding filter <{0}> with moniker <{1}>", dsFilter.FriendlyName, dsFilter.MonikerDisplayName);
          hr = m_graphBuilder.AddFilter(dsFilter.DSFilter, dsFilter.FriendlyName);
          if (hr == 0)
          {
            Log.WriteFile(Log.LogType.Capture, "SinkGraphEx:  Added filter <{0}> with moniker <{1}>", dsFilter.FriendlyName, dsFilter.MonikerDisplayName);
          }
          else
          {
            Log.WriteFile(Log.LogType.Capture, "SinkGraphEx:  Error! Failed adding filter <{0}> with moniker <{1}>", dsFilter.FriendlyName, dsFilter.MonikerDisplayName);
            Log.WriteFile(Log.LogType.Capture, "SinkGraphEx:  Error! Result code = {0}", hr);
          }

          // Support the "legacy" member variables. This could be done different using properties
          // through which the filters are accessable. More implementation independent...
          if (dsFilter.Category == "tvtuner") m_TVTuner = dsFilter.DSFilter as IAMTVTuner;
          if (dsFilter.Category == "capture") m_captureFilter = dsFilter.DSFilter;
        }
        Log.WriteFile(Log.LogType.Capture, "SinkGraphEx: Adding configured filters...DONE");

        m_IAMAnalogVideoDecoder = m_captureFilter as IAMAnalogVideoDecoder;
        InitializeTuner();
        // All filters, up-to and including the encoder filter have been added using a configuration file.
        // The rest of the filters depends on the fact if we are just viewing TV, or Timeshifting or even
        // recording. This part is however card independent and controlled by software, although this part
        // could also be configured using a definition file. If used, this could lead to the possibility
        // of having building blocks enabling the support of about every card, or combination of cards, ie
        // even (again, pretentions...) the Sigma Designs XCard could be "coupled" to the capture card...

        // Set crossbar routing, default to Tv Tuner + Audio Tuner...
        //	DsUtils.FixCrossbarRouting(m_graphBuilder, m_captureGraphBuilder, m_captureFilter, true, false, false, false,cardName);

        FilterDefinition sourceFilter;
        FilterDefinition sinkFilter;
        IPin sourcePin = null;
        IPin sinkPin = null;

        // Create pin connections. These connections are also specified in the definitions file.
        // Note that some connections might fail due to the fact that the connection is already made,
        // probably during the addition of filters to the graph (checked with GraphEdit...)
        //
        // Pin connections can be defined in two ways:
        // 1. Using the name of the pin.
        //		This method does work, but might be language dependent, meaning the connection attempt
        //		will fail because the pin cannot be found...
        // 2.	Using the 0-based index number of the input or output pin.
        //		This method is save. It simply tells to connect output pin #0 to input pin #1 for example.
        //
        // The code assumes method 1 is used. If that fails, method 2 is tried...

        Log.WriteFile(Log.LogType.Capture, "SinkGraphEx: Adding configured pin connections...");
        for (int i = 0; i < mCard.Graph.TvConnectionDefinitions.Count; i++)
        {
          sourceFilter = mCard.GetTvFilterDefinition(((ConnectionDefinition)mCard.Graph.TvConnectionDefinitions[i]).SourceCategory);
          sinkFilter = mCard.GetTvFilterDefinition(((ConnectionDefinition)mCard.Graph.TvConnectionDefinitions[i]).SinkCategory);
          if (sourceFilter == null)
          {
            Log.WriteFile(Log.LogType.Capture, true, "SinkGraphEx: Cannot find source filter for connection:{0}", i);
            continue;
          }
          if (sinkFilter == null)
          {
            Log.WriteFile(Log.LogType.Capture, true, "SinkGraphEx: Cannot find sink filter for connection:{0}", i);
            continue;
          }
          Log.WriteFile(Log.LogType.Capture, "SinkGraphEx:  Connecting <{0}>:{1} with <{2}>:{3}",
            sourceFilter.FriendlyName, ((ConnectionDefinition)mCard.Graph.TvConnectionDefinitions[i]).SourcePinName,
            sinkFilter.FriendlyName, ((ConnectionDefinition)mCard.Graph.TvConnectionDefinitions[i]).SinkPinName);


          //sourceFilter.DSFilter.FindPin(((ConnectionDefinition)mCard.ConnectionDefinitions[i]).SourcePinName, out sourcePin);
          sourcePin = DirectShowUtil.FindPin(sourceFilter.DSFilter, PinDirection.Output, ((ConnectionDefinition)mCard.Graph.TvConnectionDefinitions[i]).SourcePinName);
          if (sourcePin == null)
          {
            String strPinName = ((ConnectionDefinition)mCard.Graph.TvConnectionDefinitions[i]).SourcePinName;
            if ((strPinName.Length == 1) && (Char.IsDigit(strPinName, 0)))
            {
              sourcePin = DsFindPin.ByDirection(sourceFilter.DSFilter, PinDirection.Output, Convert.ToInt32(strPinName));
              if (sourcePin == null)
                Log.WriteFile(Log.LogType.Capture, "SinkGraphEx:   Unable to find sourcePin: <{0}>", strPinName);
              else
                Log.WriteFile(Log.LogType.Capture, "SinkGraphEx:   Found sourcePin: <{0}> <{1}>", strPinName, sourcePin.ToString());
            }
          }
          else
            Log.WriteFile(Log.LogType.Capture, "SinkGraphEx:   Found sourcePin: <{0}> ", ((ConnectionDefinition)mCard.Graph.TvConnectionDefinitions[i]).SourcePinName);

          sinkPin = GetPin(sinkFilter.DSFilter, PinDirection.Input, ((ConnectionDefinition)mCard.Graph.TvConnectionDefinitions[i]).SinkPinName);
          /*
           * //sinkFilter.DSFilter.FindPin(((ConnectionDefinition)mCard.ConnectionDefinitions[i]).SinkPinName, out sinkPin);
          sinkPin = DirectShowUtil.FindPin(sinkFilter.DSFilter, PinDirection.Input, ((ConnectionDefinition)mCard.TvConnectionDefinitions[i]).SinkPinName);
          if (sinkPin == null)
          {
            String strPinName = ((ConnectionDefinition)mCard.TvConnectionDefinitions[i]).SinkPinName;
            if ((strPinName.Length == 1) && (Char.IsDigit(strPinName, 0)))
            {
              sinkPin = DsFindPin.ByDirection(sinkFilter.DSFilter, PinDirection.Input, Convert.ToInt32(strPinName));
              if (sinkPin == null)
                Log.WriteFile(Log.LogType.Capture, "SinkGraphEx:   Unable to find sinkPin: <{0}>", strPinName);
              else
                Log.WriteFile(Log.LogType.Capture, "SinkGraphEx:   Found sinkPin: <{0}> <{1}>", strPinName, sinkPin.ToString());
            }
          }
          else
            Log.WriteFile(Log.LogType.Capture, "SinkGraphEx:   Found sinkPin: <{0}> ", ((ConnectionDefinition)mCard.TvConnectionDefinitions[i]).SinkPinName);
          */
          if (sourcePin != null && sinkPin != null)
          {
            IPin conPin;
            hr = sourcePin.ConnectedTo(out conPin);
            if (hr != 0)
              hr = m_graphBuilder.Connect(sourcePin, sinkPin);
            if (hr == 0)
              Log.WriteFile(Log.LogType.Capture, "SinkGraphEx:   Pins connected...");

            // Give warning and release pin...
            if (conPin != null)
            {
              Log.WriteFile(Log.LogType.Capture, "SinkGraphEx:   (Pin was already connected...)");
              Marshal.ReleaseComObject(conPin as Object);
              conPin = null;
              hr = 0;
            }
          }

          if (hr != 0)
          {
            Log.WriteFile(Log.LogType.Capture, "SinkGraphEx:  Error: Unable to connect Pins 0x{0:X}", hr);
            if (hr == -2147220969)
            {
              Log.WriteFile(Log.LogType.Capture, "SinkGraphEx:   -- Cannot connect: {0} or {1}", sourcePin.ToString(), sinkPin.ToString());
            }

            if (sourcePin != null) Marshal.ReleaseComObject(sourcePin); sourcePin = null;
            if (sinkPin != null) Marshal.ReleaseComObject(sinkPin); sinkPin = null;
            if (sourceFilter.DSFilter != null)
            {
              Log.WriteFile(Log.LogType.Capture, "SinkGraphEx: remove {0}", sourceFilter.FriendlyName);
              m_graphBuilder.RemoveFilter(sourceFilter.DSFilter);
              Marshal.ReleaseComObject(sourceFilter.DSFilter); sourceFilter.DSFilter = null;
            }
            RetryOtherInstances(i);
            Log.WriteFile(Log.LogType.Capture, "SinkGraphEx: RetryOtherInstances done");
          }//if (hr != 0)
        }//for (int i = 0; i < mCard.TvConnectionDefinitions.Count; i++)

        if (sinkPin != null)
          Marshal.ReleaseComObject(sinkPin);
        sinkPin = null;
        if (sourcePin != null)
          Marshal.ReleaseComObject(sourcePin);
        sourcePin = null;
        Log.WriteFile(Log.LogType.Capture, "SinkGraphEx: Adding configured pin connections...DONE");

        // Find out which filter & pin is used as the interface to the rest of the graph.
        // The configuration defines the filter, including the Video, Audio and Mpeg2 pins where applicable
        // We only use the filter, as the software will find the correct pin for now...
        // This should be changed in the future, to allow custom graph endings (mux/no mux) using the
        // video and audio pins to connect to the rest of the graph (SBE, overlay etc.)
        // This might be needed by the ATI AIW cards (waiting for ob2 to release...)
        FilterDefinition lastFilter = mCard.GetTvFilterDefinition(mCard.Graph.TvInterfaceDefinition.FilterCategory);




        // All filters and connections have been made.
        // Now fix the rest of the graph, add MUX etc.
        m_videoCaptureDevice = new VideoCaptureDevice(m_graphBuilder, m_captureGraphBuilder, m_captureFilter, lastFilter.DSFilter, (mCard.CardType == TVCapture.CardTypes.Analog_MCE), (mCard.CardType == TVCapture.CardTypes.Analog_MCE || mCard.CardType == TVCapture.CardTypes.Analog));

        m_FrameSize = m_videoCaptureDevice.GetFrameSize();

        Log.WriteFile(Log.LogType.Capture, "SinkGraphEx: Capturing:{0}x{1}", m_FrameSize.Width, m_FrameSize.Height);
        m_mpeg2Demux = null;
        if (m_videoCaptureDevice.MPEG2)
        {
          // creates the last part of the graph. Depending on timeshifting etc.
          // it will eventually connect to the lastFilter, so this object only does the last part of the graph
          m_mpeg2Demux = new MPEG2Demux(ref m_graphBuilder, m_FrameSize);
        }

        // Connect video capture->mpeg2 demuxer
        ConnectVideoCaptureToMPEG2Demuxer(); // can be done using config, or just leave it this way ;-)
        m_mpeg2Demux.CreateMappings();

        // For just MPEG2 cards, set the videoprocamp...
        //if (((mCard.CardType == TVCaptureDevice.CardTypes.Analog_MCE || 
        //     mCard.CardType == TVCaptureDevice.CardTypes.Analog)) && (!mCard.IsMCECard))
        {
          m_videoprocamp = m_captureFilter as IAMVideoProcAmp;
          if (m_videoprocamp != null)
          {
            m_videoAmp = new VideoProcAmp(m_videoprocamp);
            m_videoAmp.Contrast = m_videoAmp.ContrastDefault;
            m_videoAmp.Brightness = m_videoAmp.BrightnessDefault;
            m_videoAmp.Gamma = m_videoAmp.GammaDefault;
            m_videoAmp.Saturation = m_videoAmp.SaturationDefault;
            m_videoAmp.Sharpness = m_videoAmp.SharpnessDefault;
          }
        }

        m_graphState = State.Created;
        Log.WriteFile(Log.LogType.Capture, "SinkGraphEx:CreateGraph() OUT");

        SetQuality(3);

        SetupTeletext();
        return true;
      }
      catch (Exception ex)
      {
        Log.WriteFile(Log.LogType.Capture, true, "SinkGraphEx: Unable to create graph:{0} {1} {2}", ex.Message, ex.Source, ex.StackTrace);
        return false;
      }
    }

    void SetupTeletext()
    {
      //
      //	[							 ]		 [  tee/sink	]			 [	wst			]			[ sample	]
      //	[	capture			 ]		 [		to			]----->[	codec		]---->[ grabber	]
      //	[						vbi]---->[	sink			]			 [					]			[					]
      //
      if (GUIGraphicsContext.DX9Device == null) return;


      AddTeeSinkToGraph(m_graphBuilder); //Tee/Sink-to-Sink Converter
      IBaseFilter teesink = DirectShowUtil.GetFilterByName(m_graphBuilder, "Kernel Tee");
      if (teesink == null) return;

      AddWstCodecToGraph(m_graphBuilder);//WST Codec
      IBaseFilter wstCodec = DirectShowUtil.GetFilterByName(m_graphBuilder, "WST Codec");
      if (wstCodec == null)
      {
        m_graphBuilder.RemoveFilter(teesink);
        Marshal.ReleaseComObject(teesink);
        return;
      }

      int hr = m_captureGraphBuilder.RenderStream(new DsGuid( ClassId.PinCategoryVBI ), null, m_captureFilter, teesink, wstCodec);
      if (hr != 0)
      {
        m_graphBuilder.RemoveFilter(teesink);
        m_graphBuilder.RemoveFilter(wstCodec);
        Marshal.ReleaseComObject(teesink);
        Marshal.ReleaseComObject(wstCodec);
        return;
      }


      m_sampleGrabber = (IBaseFilter)new SampleGrabber();
      m_sampleInterface = (ISampleGrabber)m_sampleGrabber;
      m_graphBuilder.AddFilter(m_sampleGrabber, "Sample Grabber");

      AMMediaType mt = new AMMediaType();
      mt.majorType = MediaType.VBI;
      mt.subType = MediaSubTypeEx.Teletext;
      m_sampleInterface.SetCallback(this, 1);
      m_sampleInterface.SetMediaType( mt);
      m_sampleInterface.SetBufferSamples(false);
      hr = m_captureGraphBuilder.RenderStream(null, null, wstCodec, null, m_sampleGrabber);
      if (hr != 0)
      {
        m_graphBuilder.RemoveFilter(teesink);
        m_graphBuilder.RemoveFilter(wstCodec);
        m_graphBuilder.RemoveFilter(m_sampleGrabber);
        Marshal.ReleaseComObject(teesink);
        Marshal.ReleaseComObject(wstCodec);
        Marshal.ReleaseComObject(m_sampleGrabber);
        return;
      }
      Marshal.ReleaseComObject(teesink);
      Marshal.ReleaseComObject(wstCodec);
      _hasTeletext = true;
    }

    void RetryOtherInstances(int instance)
    {

      Log.WriteFile(Log.LogType.Capture, true, "SinkGraphEx: RetryOtherInstances:{0}", instance);
      FilterDefinition sourceFilter;
      FilterDefinition sinkFilter;
      IPin sourcePin = null;
      IPin sinkPin = null;

      sourceFilter = mCard.GetTvFilterDefinition(((ConnectionDefinition)mCard.Graph.TvConnectionDefinitions[instance]).SourceCategory);
      sinkFilter = mCard.GetTvFilterDefinition(((ConnectionDefinition)mCard.Graph.TvConnectionDefinitions[instance]).SinkCategory);
      if (sourceFilter == null)
      {
        Log.WriteFile(Log.LogType.Capture, true, "SinkGraphEx: Cannot find source filter for connection:{0}", instance);
        return;
      }
      if (sinkFilter == null)
      {
        Log.WriteFile(Log.LogType.Capture, true, "SinkGraphEx: Cannot find sink filter for connection:{0}", instance);
        return;
      }

      Log.WriteFile(Log.LogType.Capture, true, "SinkGraphEx: find instances of :{0} instances", sourceFilter.FriendlyName);
      foreach (string key in AvailableFilters.Filters.Keys)
      {
        int hr;
        Filter filter;
        ArrayList al = AvailableFilters.Filters[key] as System.Collections.ArrayList;
        filter = (Filter)al[0];
        if (filter.Name.Equals(sourceFilter.FriendlyName))
        {
          Log.WriteFile(Log.LogType.Capture, true, "SinkGraphEx: found :{0} instances", al.Count);
          for (int filterInstance = 0; filterInstance < al.Count; ++filterInstance)
          {
            filter = (Filter)al[filterInstance];
            Log.WriteFile(Log.LogType.Capture, true, "SinkGraphEx: try instance :{0} {1}", filterInstance, filter.MonikerString);
            sourceFilter.MonikerDisplayName = filter.MonikerString;
            sourceFilter.DSFilter = Marshal.BindToMoniker(sinkFilter.MonikerDisplayName) as IBaseFilter;
            hr = m_graphBuilder.AddFilter(sourceFilter.DSFilter, sourceFilter.FriendlyName);
            sourcePin = DirectShowUtil.FindPin(sourceFilter.DSFilter, PinDirection.Output, ((ConnectionDefinition)mCard.Graph.TvConnectionDefinitions[instance]).SourcePinName);
            if (sourcePin == null)
            {
              String strPinName = ((ConnectionDefinition)mCard.Graph.TvConnectionDefinitions[instance]).SourcePinName;
              if ((strPinName.Length == 1) && (Char.IsDigit(strPinName, 0)))
              {
                sourcePin = DsFindPin.ByDirection(sourceFilter.DSFilter, PinDirection.Output, Convert.ToInt32(strPinName));
                if (sourcePin == null)
                  Log.WriteFile(Log.LogType.Capture, "SinkGraphEx:   Unable to find sourcePin: <{0}>", strPinName);
                else
                  Log.WriteFile(Log.LogType.Capture, "SinkGraphEx:   Found sourcePin: <{0}> <{1}>", strPinName, sourcePin.ToString());
              }
            }
            else
              Log.WriteFile(Log.LogType.Capture, "SinkGraphEx:   Found sourcePin: <{0}> ", ((ConnectionDefinition)mCard.Graph.TvConnectionDefinitions[instance]).SourcePinName);

            sinkPin = DirectShowUtil.FindPin(sinkFilter.DSFilter, PinDirection.Input, ((ConnectionDefinition)mCard.Graph.TvConnectionDefinitions[instance]).SinkPinName);
            if (sinkPin == null)
            {
              String strPinName = ((ConnectionDefinition)mCard.Graph.TvConnectionDefinitions[instance]).SinkPinName;
              if ((strPinName.Length == 1) && (Char.IsDigit(strPinName, 0)))
              {
                sinkPin = DsFindPin.ByDirection(sinkFilter.DSFilter, PinDirection.Input, Convert.ToInt32(strPinName));
                if (sinkPin == null)
                  Log.WriteFile(Log.LogType.Capture, "SinkGraphEx:   Unable to find sinkPin: <{0}>", strPinName);
                else
                  Log.WriteFile(Log.LogType.Capture, "SinkGraphEx:   Found sinkPin: <{0}> <{1}>", strPinName, sinkPin.ToString());
              }
            }
            else
              Log.WriteFile(Log.LogType.Capture, "SinkGraphEx:   Found sinkPin: <{0}> ", ((ConnectionDefinition)mCard.Graph.TvConnectionDefinitions[instance]).SinkPinName);

            if (sourcePin != null && sinkPin != null)
            {
              IPin conPin;
              hr = sourcePin.ConnectedTo(out conPin);
              if (hr != 0)
                hr = m_graphBuilder.Connect(sourcePin, sinkPin);
              if (hr == 0)
                Log.WriteFile(Log.LogType.Capture, "SinkGraphEx:   Pins connected...");

              // Give warning and release pin...
              if (conPin != null)
              {
                Log.WriteFile(Log.LogType.Capture, "SinkGraphEx:   (Pin was already connected...)");
                Marshal.ReleaseComObject(conPin as Object);
                conPin = null;
                hr = 0;
              }
            }

            if (hr != 0)
            {
              Log.WriteFile(Log.LogType.Capture, "SinkGraphEx:  Error: Unable to connect Pins 0x{0:X}", hr);

              if (sourcePin != null) Marshal.ReleaseComObject(sourcePin); sourcePin = null;
              if (sinkPin != null) Marshal.ReleaseComObject(sinkPin); sinkPin = null;
              if (sourceFilter.DSFilter != null)
              {
                m_graphBuilder.RemoveFilter(sourceFilter.DSFilter);
                Marshal.ReleaseComObject(sourceFilter.DSFilter);
                sourceFilter.DSFilter = null;
              }
            }//if (hr != 0)
            else
            {
              if (sinkPin != null)
                Marshal.ReleaseComObject(sinkPin);
              sinkPin = null;

              if (sourcePin != null)
                Marshal.ReleaseComObject(sourcePin);
              sourcePin = null;
              return;
            }
          }//for (int filterInstance=0; filterInstance < al.Count;++filterInstance)
        }//if (filter.Name.Equals(sinkFilter.FriendlyName))
      }//foreach (string key in AvailableFilters.Filters.Keys)
    }//void RetryOtherInstances(int instance)

    public override void DeleteGraph()
    {
      if (m_graphState < State.Created) return;
      int hr;


      _grabTeletext = false;
      m_iPrevChannel = -1;
      Log.WriteFile(Log.LogType.Capture, "SinkGraph:DeleteGraph()");
      StopRecording();
      StopTimeShifting();
      StopViewing();

      GUIGraphicsContext.OnGammaContrastBrightnessChanged -= new VideoGammaContrastBrightnessHandler(OnGammaContrastBrightnessChanged);
      if (Vmr9 != null)
      {
        Vmr9.RemoveVMR9();
        Vmr9.Release();
        Vmr9 = null;
      }
      if (Vmr7 != null)
      {
        Vmr7.RemoveVMR7();
        Vmr7 = null;
      }
      m_IAMAnalogVideoDecoder = null;


      if (m_videoprocamp != null)
      {
        m_videoAmp = null;
        m_videoprocamp = null;
      }
      if (m_mpeg2Demux != null)
      {
        m_mpeg2Demux.CloseInterfaces();
        m_mpeg2Demux = null;
      }

      if (m_videoCaptureDevice != null)
      {
        m_videoCaptureDevice.CloseInterfaces();
        m_videoCaptureDevice = null;
      }

      if (m_TVTuner != null)
        Marshal.ReleaseComObject(m_TVTuner); m_TVTuner = null;

      m_sampleInterface = null;
      if (m_sampleGrabber != null)
      {
        while ((hr = Marshal.ReleaseComObject(m_sampleGrabber)) > 0) ;
        m_sampleGrabber = null;
      }


      if (m_graphBuilder != null)
        DirectShowUtil.RemoveFilters(m_graphBuilder);

      if (m_captureFilter != null)
        Marshal.ReleaseComObject(m_captureFilter); m_captureFilter = null;


      if (_rotEntry != null)
      {
        _rotEntry.Dispose();
      }
      _rotEntry = null;

      foreach (FilterDefinition dsFilter in mCard.Graph.TvFilterDefinitions)
      {
        //string strfName = dsFilter.Category;
        //FilterDefinition dsFilter = mCard.TvFilterDefinitions[strfName] as FilterDefinition;
        if (dsFilter.DSFilter != null)
          Marshal.ReleaseComObject(dsFilter.DSFilter);
        //((FilterDefinition)mCard.TvFilterDefinitions[strfName]).DSFilter = null;
        //dsFilter = null;
      }

      if (m_captureGraphBuilder != null)
        Marshal.ReleaseComObject(m_captureGraphBuilder); m_captureGraphBuilder = null;

      if (m_graphBuilder != null)
        Marshal.ReleaseComObject(m_graphBuilder); m_graphBuilder = null;

      _hasTeletext = false;
      m_graphState = State.None;
      return;
    }

    IPin GetPin(IBaseFilter filter, PinDirection direction, string pinName)
    {
      if (direction == PinDirection.Input)
      {
        if (String.Compare(pinName, "%tvtuner%", true) == 0)
        {
          IPin pin = FindCrossBarPin(filter, PhysicalConnectorType.Video_Tuner);
          if (pin != null) return pin;
        }
        if (String.Compare(pinName, "%audiotuner%", true) == 0)
        {
          IPin pin = FindCrossBarPin(filter, PhysicalConnectorType.Audio_Tuner);
          if (pin != null) return pin;
        }
      }

      IPin sinkPin = DirectShowUtil.FindPin(filter, direction, pinName);
      if (sinkPin == null)
      {
        if ((pinName.Length == 1) && (Char.IsDigit(pinName, 0)))
        {
          sinkPin = DsFindPin.ByDirection(filter, PinDirection.Input, Convert.ToInt32(pinName));
          if (sinkPin == null)
            Log.WriteFile(Log.LogType.Capture, "SinkGraphEx:   Unable to find pin: <{0}>", pinName);
          else
            Log.WriteFile(Log.LogType.Capture, "SinkGraphEx:   Found pin: <{0}> <{1}>", pinName, sinkPin.ToString());
        }
      }
      else
        Log.WriteFile(Log.LogType.Capture, "SinkGraphEx:   Found pin: <{0}> ", pinName);
      return sinkPin;
    }

    IPin FindCrossBarPin(IBaseFilter filter,PhysicalConnectorType inputPinType)
    {
      IAMCrossbar crossbar = filter as IAMCrossbar;
      if (crossbar == null) return null;
      int outputPins, inputPins;
      crossbar.get_PinCounts(out outputPins, out inputPins);
      for (int i = 0; i < inputPins; ++i)
      {
        int relatedPin;
        PhysicalConnectorType physicalTypeIn;			// type of input pin
        crossbar.get_CrossbarPinInfo(true, i, out relatedPin, out physicalTypeIn);
        if (physicalTypeIn == inputPinType)
        {
          IPin pin= DsFindPin.ByDirection(filter, PinDirection.Input, i);
          return pin;
        }
      }
      return null;
    }

    IBaseFilter GetFilter(string filterName)
    {
      if (String.Compare(filterName, "%soundcard%", true) != 0) return null;
      Filters filters = new Filters();
      FilterCollection audioInputs = filters.AudioInputDevices;

      Log.WriteFile(Log.LogType.Capture, "SinkGraphEx:  adding filter <{0}> with moniker <{1}>",audioInputs[0].Name , audioInputs[0].MonikerString);

      IBaseFilter audioInputFilter = Marshal.BindToMoniker(audioInputs[0].MonikerString) as IBaseFilter;
      return audioInputFilter ;
    }

    #endregion Overrides

    #region Obsolete But Probably Needed Again

    // #MW#
    // Not used anymore. Might be needed though to support these cards, or is a generic approach possible?
    // What if the connection order always assures that the TVTuner is connected first, meaning the
    // outputs of the crossbar are NOT connected to any other filter???????
    //
    // Extra Note:
    // Just found out that once you load some of the filters, some connectiona are already made, even
    // without explicit calls, so the crossbar might be connected already...
    void ConnectTVTunerOutputs()
    {
      // AverMedia MCE card has a bug. It will only connect the TV Tuner->crossbar if
      // the crossbar outputs are disconnected
      // same for the winfast pvr 2000
      Log.WriteFile(Log.LogType.Capture, "SinkGraphEx:ConnectTVTunerOutputs()");

      //find crossbar
      int hr;
      DsGuid cat;
      Guid iid;
      object o = null;
      cat = new DsGuid(FindDirection.UpstreamOnly);
      iid = typeof(IAMCrossbar).GUID;
      hr = m_captureGraphBuilder.FindInterface(cat, null, m_captureFilter,  iid, out o);
      if (hr != 0 || o == null)
      {
        Log.WriteFile(Log.LogType.Capture, "SinkGraphEx:no crossbar found");
        return; // no crossbar found?
      }

      IAMCrossbar crossbar = o as IAMCrossbar;
      if (crossbar == null)
      {
        Log.WriteFile(Log.LogType.Capture, "SinkGraphEx:no crossbar found");
        return;
      }

      //disconnect the output pins of the crossbar->video capture filter
      //			Log.WriteFile(Log.LogType.Capture,"SinkGraphEx:disconnect crossbar outputs");
      DirectShowUtil.DisconnectOutputPins(m_graphBuilder, (IBaseFilter)crossbar);

      //connect the output pins of the tvtuner
      //			Log.WriteFile(Log.LogType.Capture,"SinkGraphEx:connect tvtuner outputs");
      bool bAllConnected = DirectShowUtil.RenderOutputPins(m_graphBuilder, (IBaseFilter)m_TVTuner);
      if (!bAllConnected)
        Log.WriteFile(Log.LogType.Capture, "SinkGraphEx:FAILED, not all pins connected");

      //reconnect the output pins of the crossbar
      //			Log.WriteFile(Log.LogType.Capture,"SinkGraphEx:reconnect crossbar output pins");

      bAllConnected = DirectShowUtil.RenderOutputPins(m_graphBuilder, (IBaseFilter)crossbar);
      if (!bAllConnected)
        Log.WriteFile(Log.LogType.Capture, "SinkGraphEx:FAILED, not all pins connected");

      if (o != null)
      {
        crossbar = null;
        Marshal.ReleaseComObject(o);
        o = null;
      }
    }

    #endregion

    #region ISampleGrabberCB Members

    public int SampleCB(double SampleTime, IMediaSample pSample)
    {
      // TODO:  Add SinkGraphEx.SampleCB implementation
      return 0;
    }

    public int BufferCB(double SampleTime, System.IntPtr pBuffer, int BufferLen)
    {

      if (!_grabTeletext) return 0;
      if (pBuffer == IntPtr.Zero) return 0;
      if (BufferLen < 43) return 0;

      TeletextGrabber.SaveAnalogData(pBuffer, BufferLen);
      return 0;
    }

    #endregion
  }
}
#endif