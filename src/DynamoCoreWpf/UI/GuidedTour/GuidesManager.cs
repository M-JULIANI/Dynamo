﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls.Primitives;
using Dynamo.ViewModels;
using Dynamo.Wpf.Views.GuidedTour;
using Newtonsoft.Json;
using Res = Dynamo.Wpf.Properties.Resources;

namespace Dynamo.Wpf.UI.GuidedTour
{
    /// <summary>
    /// This class will manage the Guides read from the json file
    /// </summary>
    public sealed class GuidesManager
    {
        /// <summary>
        /// This property contains the list of Guides read from the json file
        /// </summary>
        public List<Guide> Guides;

        /// <summary>
        /// currentGuide will contain the Guide being played
        /// </summary>
        private Guide currentGuide;
        private UIElement mainRootElement;
        private GuideBackground guideBackgroundElement;

        private DynamoViewModel dynamoViewModel;

        private const double ExitTourVerticalOffset = 30;
        private const double ExitTourHorizontalOffset = 0;

        /// <summary>
        /// GuidesManager Constructor that will read all the guides/steps from and json file and subscribe handlers for the Start and Finish events
        /// </summary>
        /// <param name="root">root item of the main Dynamo Window </param>
        public GuidesManager(UIElement root, DynamoViewModel dynViewModel)
        {
            mainRootElement = root;
            dynamoViewModel = dynViewModel;
            guideBackgroundElement = Guide.FindChild(root, "GuidesBackground") as GuideBackground;

            Guides = new List<Guide>();
            CreateGuideSteps(@"UI\GuidedTour\dynamo_guides.json");

            //Subscribe the handlers when the Tour is started and finished, the handlers are unsubscribed in the method TourFinished()
            GuideFlowEvents.GuidedTourStart += TourStarted;
            GuideFlowEvents.GuidedTourFinish += TourFinished;
        }

        /// <summary>
        /// This method will launch the tour when the user clicks in the Help->Interactive Guides->Guide
        /// </summary>
        /// <param name="tourName"></param>
        public void LaunchTour(string tourName)
        {
            GuideFlowEvents.OnGuidedTourStart(tourName);
        }

        /// <summary>
        /// This method will be executed when the OnGuidedTourStart event is raised
        /// </summary>
        /// <param name="args">This parameter will contain the GuideName as a string</param>
        private void TourStarted(GuidedTourStateEventArgs args)
        {
            currentGuide = (from guide in Guides where guide.Name.Equals(args.GuideName) select guide).FirstOrDefault();
            if (currentGuide != null)
            {
                //Show background overlay
                guideBackgroundElement.Visibility = Visibility.Visible;
                currentGuide.Initialize();
                currentGuide.Play();
            }
        }

        /// <summary>
        /// This method will be executed when the OnGuidedTourFinish event is raised
        /// </summary>
        /// <param name="args">This parameter will contain the GuideName as a string</param>
        private void TourFinished(GuidedTourStateEventArgs args)
        {
            currentGuide = (from guide in Guides where guide.Name.Equals(args.GuideName) select guide).FirstOrDefault();
            if (currentGuide != null)
            {
                foreach (Step tmpStep in currentGuide.GuideSteps)
                {
                    tmpStep.StepClosed -= Popup_StepClosed;
                }
                currentGuide.ClearGuide();
                GuideFlowEvents.GuidedTourStart -= TourStarted;
                GuideFlowEvents.GuidedTourFinish -= TourFinished;

                //Hide guide background overlay
                guideBackgroundElement.Visibility = Visibility.Hidden;

            }

        }

        /// <summary>
        /// This method will read all the guides information from a json file located in the same directory than the DynamoSandbox.exe is located.
        /// </summary>
        /// <param name="jsonFile">Full path of the json file location containing information about the Guides and Steps</param>
        private static List<Guide> ReadGuides(string jsonFile)
        {
            string jsonString = string.Empty;
            using (StreamReader r = new StreamReader(jsonFile))
            {
                jsonString = r.ReadToEnd();
            }

            //Deserialize all the information read from the json file
            return JsonConvert.DeserializeObject<List<Guide>>(jsonString);
        }

        /// <summary>
        /// This method will create all the Guide and add them to the Guides List based in the deserialized info gotten from the json file passed as parameter
        /// </summary>
        /// <param name="jsonFile">Full path of the json file location containing information about the Guides and Steps</param>
        private void CreateGuideSteps(string jsonFile)
        { 
            int totalTooltips = 0;

            foreach (Guide guide in GuidesManager.ReadGuides(jsonFile))
            {
                Guide newGuide = new Guide()
                {
                    Name = guide.Name,
                };

                totalTooltips = (from step in guide.GuideSteps
                                 where step.StepType == Step.StepTypes.TOOLTIP ||
                                       step.StepType == Step.StepTypes.SURVEY
                                 select step).Count();

                foreach (Step step in guide.GuideSteps)
                {
                    HostControlInfo hostControlInfo = CreateHostControl(step);               
                    Step newStep = CreateStep(step, hostControlInfo, totalTooltips);
                    if(newStep != null)
                    {
                        //The step is added to the new Guide being created
                        newGuide.GuideSteps.Add(newStep);

                        //We subscribe the handler to the StepClosed even, so every time the popup is closed then this method will be called.
                        newStep.StepClosed += Popup_StepClosed;
                    }
                }
                Guides.Add(newGuide);
            }
        }

        /// <summary>
        /// This method will return a new HostControlInfo object populated with the information passed as parameter
        /// Basically this method store the information coming from Step and search the UIElement in the main WPF VisualTree
        /// </summary>
        /// <param name="jsonStepInfo">Step that contains all the info deserialized from the Json file</param>
        /// <returns></returns>
        private HostControlInfo CreateHostControl(Step jsonStepInfo)
        {
            var popupInfo = new HostControlInfo()
            {
                PopupPlacement = jsonStepInfo.HostPopupInfo.PopupPlacement,
                HostUIElementString = jsonStepInfo.HostPopupInfo.HostUIElementString,
                HostUIElement = mainRootElement,
                VerticalPopupOffSet = jsonStepInfo.HostPopupInfo.VerticalPopupOffSet,
                HorizontalPopupOffSet = jsonStepInfo.HostPopupInfo.HorizontalPopupOffSet
            };

            //The host_ui_element read from the json file need to exists otherwise the host will be null
            UIElement hostUIElement = Guide.FindChild(mainRootElement, popupInfo.HostUIElementString);
            if (hostUIElement != null)
                popupInfo.HostUIElement = hostUIElement;

            return popupInfo;
        }

        /// <summary>
        /// Creates a new Step with the information passed as parameter (the only extra-information calculated is the TotalTooltips, the Text for Title and Content and other properties like the Suvey.RatingTextTitle
        /// </summary>
        /// <param name="jsonStepInfo">Step that contains all the info deserialized from the Json file</param>
        /// <param name="hostControlInfo">Information of the host read previously</param>
        /// <param name="totalTooltips">Total number of tooltips, calculated once we deserialized all the steps from json</param>
        /// <returns></returns>
        private Step CreateStep(Step jsonStepInfo, HostControlInfo hostControlInfo, int totalTooltips)
        {
            Step newStep = null;
            //This section will retrive the strings from the Resources.resx file
            var formattedText = Res.ResourceManager.GetString(jsonStepInfo.StepContent.FormattedText);
            var title = Res.ResourceManager.GetString(jsonStepInfo.StepContent.Title);

            switch (jsonStepInfo.StepType)
            {
                case Step.StepTypes.TOOLTIP:
                    newStep = new Tooltip(hostControlInfo, jsonStepInfo.Width, jsonStepInfo.Height, jsonStepInfo.TooltipPointerDirection)
                    {
                        Name = jsonStepInfo.Name,
                        Sequence = jsonStepInfo.Sequence,
                        TotalTooltips = totalTooltips,
                        StepType = Step.StepTypes.TOOLTIP,
                        StepContent = new Content()
                        {
                            FormattedText = formattedText,
                            Title = title
                        }
                    };                   
                    break;
                case Step.StepTypes.SURVEY:
                    newStep = new Survey(hostControlInfo, jsonStepInfo.Width, jsonStepInfo.Height)
                    {
                        Sequence = jsonStepInfo.Sequence,
                        ContentWidth = 300,
                        RatingTextTitle = formattedText.ToString(),
                        StepType = Step.StepTypes.SURVEY,
                        IsRatingVisible = dynamoViewModel.Model.PreferenceSettings.IsADPAnalyticsReportingApproved,
                        StepContent = new Content()
                        {
                            FormattedText = formattedText,
                            Title = title
                        }
                    };

                    //Due that the RatingTextTitle property is just for Survey then we need to set the property using reflection
                    foreach (var extraContent in jsonStepInfo.StepExtraContent)
                    {
                        // Get the Type object corresponding to Step.
                        Type myType = typeof(Survey);
                        // Get the PropertyInfo object by passing the property name.
                        PropertyInfo myPropInfo = myType.GetProperty(extraContent.Property);
                        if (myPropInfo != null)
                        {
                            //Retrieve the string value from the Resources.resx file
                            var valueStr = Res.ResourceManager.GetString(extraContent.Value);
                            myPropInfo.SetValue(newStep, valueStr);
                        }
                    }
                    break;
                case Step.StepTypes.WELCOME:
                    newStep = new Welcome(hostControlInfo, jsonStepInfo.Width, jsonStepInfo.Height)
                    {
                        Sequence = jsonStepInfo.Sequence,
                        StepType = Step.StepTypes.WELCOME,
                        StepContent = new Content()
                        {
                            FormattedText = formattedText,
                            Title = title
                        }
                    };
                    break;
            }//StepType

            return newStep;
        }

        private void Popup_StepClosed(string name, Step.StepTypes stepType)
        {
            GuideFlowEvents.OnGuidedTourFinish(currentGuide.Name);

            //The exit tour popup will be shown only when a popup (doesn't apply for survey) is closed or when the tour is closed. 
            if(stepType != Step.StepTypes.SURVEY)
                CreateRealTimeInfoWindow();
        }

        private void CreateRealTimeInfoWindow()
        {
            //Search a UIElement with the Name "statusBarPanel" inside the Dynamo VisualTree
            UIElement hostUIElement = Guide.FindChild(mainRootElement, "statusBarPanel");

            //Creates the RealTimeInfoWindow popup and set up all the needed values to show the popup over the Dynamo workspace
            var exitTourPopup = new RealTimeInfoWindow()
            {
                VerticalOffset = ExitTourVerticalOffset,
                HorizontalOffset = ExitTourHorizontalOffset,
                Placement = PlacementMode.Center,
                TextContent = Res.ExitTourWindowContent
            };

            if (hostUIElement != null)
                exitTourPopup.PlacementTarget = hostUIElement;
            exitTourPopup.IsOpen = true;
        }
    }
}
