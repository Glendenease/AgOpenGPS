﻿//Please, if you use this, share the improvements

using System.IO.Ports;
using System;
using System.Collections.Generic;
using System.Windows.Forms;
using SharpGL;
using System.Drawing;
using System.Text;

namespace AgOpenGPS
{
    public partial class FormGPS
    {
        public static string portName = "COM GPS...";
        public static int baudRate = 4800;

        public static string portNameArduino = "COM SectionControl...";
        public static int baudRateArduino = 9600;

        int startCounter = 0;

        //used to decide to autoconnect arduino this run
        public bool wasArduinoConnectedLastRun = false;

        //very first fix to setup grid etc
        public bool isFirstFixPositionSet = false;

        //a distance between previous and current fix
        private double distance = 0.0;
        public double sectionDistance = 0;

        //how far travelled since last section was added, section points
        double sectionTriggerDistance = 0;
        double currentSectionNorthing = 0;
        double currentSectionEasting = 0;
        public double prevSectionNorthing = 0;
        public double prevSectionEasting = 0;

        //tally counters for display
        public double totalSquareMeters = 0;
        public double totalDistance = 0;
        public double userTotalSquareMeters = 0;
        public double userDistance = 0;

        //are we still getting valid data from GPS, resets to 0 in NMEA RMC block, watchdog 
        public int recvCounter = 20;

        //array index for previous northing and easting positions
        private static int numPrevs = 10;
        public double[] prevNorthing = new double[numPrevs];
        public double[] prevEasting = new double[numPrevs];

        //Low speed detection variables
        public double prevNorthingLowSpeed = 0;
        public double prevEastingLowSpeed = 0;
        double distanceLowSpeed = 0;

        //public double prevFixHeading;

        //public string recvSentence = "InitalSetting";
        //public string recvSentenceArduino = "Section Control";

        public StringBuilder recvSentence = new StringBuilder();
        public StringBuilder recvSentenceArduino = new StringBuilder("SectionControl");

        public string recvSentenceSettings = "InitalSetting";
        public string recvSentenceSettingsArduino = "Section Control";



        //serial port gps is connected to
        public SerialPort sp = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One);

        //serial port Arduino is connected to
        public SerialPort spArduino = new SerialPort(portNameArduino, baudRateArduino, Parity.None, 8, StopBits.One);

        //how many fix updates per sec
        public int rmcUpdateHz = 5;

       //individual points
        //List<CPointData> pointList = new List<CPointData>();

        private void SectionControlOutToArduino()
        {
            if (!spArduino.IsOpen) return;

            byte set = 1;
            byte reset = 254;
            bufferArd[0] = 0;

            for (int j = 0; j < MAXSECTIONS; j++)
            {
                if (section[j].isSectionOn) bufferArd[0] = (byte)(bufferArd[0] | set);
                else bufferArd[0] = (byte)(bufferArd[0] & reset);

                set = (byte)(set << 1);

                reset = (byte)(reset << 1);
                reset = (byte)(reset + 1);
            }

            //Tell Arduino to turn section on or off accordingly
            if (isMasterSectionOn & spArduino.IsOpen)
            {
                try { spArduino.Write(bufferArd, 0, 1); }
                catch (Exception) { SerialPortCloseArduino(); }
            }
            else
            {
                bufferArd[0] = 0;

                try { spArduino.Write(bufferArd, 0, 1); }
                catch (Exception) { 
                    SerialPortCloseArduino(); }
                return;
            }


        }

        
        //called by the openglDraw routine.
        private void UpdateFixPosition()
        {
           //if saving a file ignore any movement
            if (isSavingFile) return;
            //textBoxRcv.Text = recvSentence;

            //add what has been rec'd to the nmea buffer
            pn.rawBuffer += recvSentence.ToString();

            //empty the received sentence
            recvSentence.Clear();

            //parse the line received GGA and RMC
            pn.ParseNMEA();

            //if its a valid fix data for RMC
            if (pn.updatedRMC)
            {
                //recvSentence = recvSentence;
                //this is the current position taken from the latest sentence
                //CPointData pd = new CPointData(pn.northing, pn.easting, pn.speed, pn.headingTrue);

                if (!isFirstFixPositionSet)
                {
                    //Draw a grid once we know where in the world we are.
                    isFirstFixPositionSet = true;
                    worldGrid.CreateWorldGrid(pn.northing, pn.easting);

                   //most recent fixes
                    prevEasting[0] = pn.easting;
                    prevNorthing[0] = pn.northing;

                     //save a copy of previous positions for cam heading of desired filtering or delay
                    for (int x = 0; x > numPrevs; x++)
                    {
                        prevNorthing[x] = prevNorthing[0];
                        prevEasting[x] = prevEasting[0];
                    }

                    //prevFixHeading = fixHeading;

                    prevSectionEasting = pn.easting;
                    prevSectionNorthing = pn.northing;

                    prevNorthingLowSpeed = pn.northing;
                    prevEastingLowSpeed = pn.easting;

                    return;
                }

 
 
                if (pn.speed < 2.0) distanceLowSpeed = pn.Distance(pn.northing, pn.easting, prevNorthingLowSpeed, prevEastingLowSpeed);
                else distanceLowSpeed = -1;

                if (distanceLowSpeed > 0.3 | pn.speed > 2.0 | startCounter < 50)
                {
                    startCounter++;
                   //determine fix positions and heading
                    fixPosX = (pn.easting);
                    fixPosZ = (pn.northing);

                    prevNorthingLowSpeed = pn.northing;
                    prevEastingLowSpeed = pn.easting;


                    //in radians
                    fixHeading = Math.Atan2(pn.easting - prevEasting[1], pn.northing - prevNorthing[1]);
                    if (fixHeading < 0) fixHeading += Math.PI * 2.0;

                    //in radians
                    if (vehicle.isHitched)
                    {
                        fixHeadingSection = Math.Atan2(pn.easting - prevEasting[rmcUpdateHz * 1], pn.northing - prevNorthing[rmcUpdateHz * 1]);
                        if (fixHeadingSection < 0) fixHeadingSection += Math.PI * 2.0;
                    }
                    else fixHeadingSection = fixHeading;

                    //in degrees for glRotate opengl methods. 
                    //can be filtered for smoother display by higher prevEastings and prevNorthings
                    fixHeadingCam = Math.Atan2(pn.easting - prevEasting[4], pn.northing - prevNorthing[4]);
                    if (fixHeadingCam < 0) fixHeadingCam += Math.PI * 2.0;
                    fixHeadingCam = fixHeadingCam * 180.0 / Math.PI;

                    fixHeadingDelta = fixHeading - fixHeadingSection;
                    
                    //lblTest.Text = Convert.ToString(Math.Round(fixHeadingDelta, 3));

 
                    //check to make sure the grid is big enough
                    worldGrid.checkZoomWorldGrid(pn.northing, pn.easting);

                    //precalc the sin and cos of heading * -1
                    sinHeading = Math.Sin(-fixHeadingSection);
                    cosHeading = Math.Cos(-fixHeadingSection);

                    //calc distance travelled since last GPS fix
                    distance = pn.Distance(pn.northing, pn.easting, prevNorthing[0], prevEasting[0]);

                    //calculate how far since the last section triangle was made
                    currentSectionNorthing = pn.northing + vehicle.toolForeAft * cosHeading;
                    currentSectionEasting = pn.easting + vehicle.toolForeAft * -sinHeading;

                    //To prevent drawing high numbers of triangles, determine and test before drawing vertex
                    sectionTriggerDistance = pn.Distance(currentSectionNorthing, currentSectionEasting, prevSectionNorthing, prevSectionEasting);

                    //speed compensated min length limit triangles. The faster you go, the less of them
                    if (sectionTriggerDistance > (pn.speed / 7 + 0.3))
                    {
                        //add the pathpoint
                        if (isJobStarted && isMasterSectionOn)
                        {
                            //save the north & east as previous
                            prevSectionNorthing = currentSectionNorthing;
                            prevSectionEasting = currentSectionEasting;

                            //send the current and previous GPS fore/aft corrected fix to each section
                            for (int j = 0; j < vehicle.numberOfSections; j++)
                            {
                                if (section[j].isSectionOn)
                                {
                                    section[j].AddPathPoint(currentSectionNorthing, currentSectionEasting, cosHeading, sinHeading);

                                    //area is made up of square meters in each section
                                    totalSquareMeters += sectionTriggerDistance * section[j].sectionWidth;
                                }
                            }
                        }
                    }


                    //distance tally, calculated based on fixposition updates, NOT sections. Runs continuously.
                    totalDistance += distance;

                    //userDistance can be reset
                    userDistance += distance;

                    //save a copy of previous positions for cam heading of desired filtering or delay
                    for (int x = numPrevs - 1; x > 0; x--)
                    {
                        prevNorthing[x] = prevNorthing[x - 1];
                        prevEasting[x] = prevEasting[x - 1];
                    }

                    //most recent fixes
                    prevEasting[0] = pn.easting;
                    prevNorthing[0] = pn.northing;

                    //label5.Text = Convert.ToString(Math.Round(prevFixHeading - fixHeading,3));

                    //prevFixHeading = fixHeading;
                }
 
                 //openGLControl.DoRender();
            }

            //a GGA sentnece rec'd

            if (pn.updatedGGA)
            {
            }

            
        }//end of UppdateFixPosition

        //called by the delegate every time a chunk is rec'd
        private void SerialLineReceived(string sentence)
        {
            //spit it out no matter what it says
            recvSentence.Append(sentence);
            recvSentenceSettings = recvSentence.ToString();
            //textBoxRcv.Text = recvSentence.ToString(); 
            textBoxRcv.Text = pn.theSent;
        }
 
        //Arduino port called by the delegate every time
        private void SerialLineReceivedArduino(string sentence)
        {
            //spit it out no matter what it says
            recvSentenceArduino.Append(sentence);
            recvSentenceSettingsArduino = sentence;

            if (txtBoxRecvArduino.TextLength > 10) txtBoxRecvArduino.Text = "";
            txtBoxRecvArduino.Text += recvSentenceArduino;

        }

        #region ArduinoSerialPort //--------------------------------------------------------------------

        //the delegate for thread
        private delegate void LineReceivedEventHandlerArduino(string sentence);

        //Arduino serial port receive in its own thread
        private void sp_DataReceivedArduino(object sender, System.IO.Ports.SerialDataReceivedEventArgs e)
        {
            if (spArduino.IsOpen)
            {
                try
                {
                    System.Threading.Thread.Sleep(100);
                    string sentence = spArduino.ReadExisting();
                    this.BeginInvoke(new LineReceivedEventHandlerArduino(SerialLineReceivedArduino), sentence);
                }
                //this is bad programming, it just ignores errors until its hooked up again.
                catch (Exception) { }
                
            }
        }

        //open the Arduino serial port
        public void SerialPortOpenArduino()
        {
            if (!spArduino.IsOpen)
            {
                spArduino.PortName = portNameArduino;
                spArduino.BaudRate = baudRateArduino;
                spArduino.DataReceived += sp_DataReceivedArduino;
            }

            try { spArduino.Open(); }
            catch (Exception exc)
            { MessageBox.Show(exc.Message + "\n\r" + "\n\r" + "Go to Settings -> COM Ports to Fix", "No Arduino Port Active");

            //update port status label
            stripPortArduino.Text = "* *";
            stripOnlineArduino.Value = 1;
            stripPortArduino.ForeColor = Color.Red;

            Properties.Settings.Default.setPort_wasArduinoConnected = false;
            Properties.Settings.Default.Save();
            }

            if (spArduino.IsOpen)
            {
                spArduino.DiscardOutBuffer();
                spArduino.DiscardInBuffer();

                //update port status label
                stripPortArduino.Text = portNameArduino;
                stripPortArduino.ForeColor = Color.ForestGreen;
                stripOnlineArduino.Value = 100;


                Properties.Settings.Default.setPort_portNameArduino = portNameArduino;
                Properties.Settings.Default.setPort_wasArduinoConnected = true;
                Properties.Settings.Default.Save();
            }
        }

        public void SerialPortCloseArduino()
        {
            if (spArduino.IsOpen)
            {
                spArduino.DataReceived -= sp_DataReceivedArduino;
                try { spArduino.Close(); }
                catch (Exception exc) { MessageBox.Show(exc.Message, "Connection already terminated??"); }
                
                //update port status label
                stripPortArduino.Text = "* *";
                stripOnlineArduino.Value = 1;
                stripPortArduino.ForeColor = Color.Red;

                Properties.Settings.Default.setPort_wasArduinoConnected = false;
                Properties.Settings.Default.Save();

                spArduino.Dispose();
            }

        }
        #endregion


        #region GPS SerialPort //--------------------------------------------------------------------------

        private delegate void LineReceivedEventHandler(string sentence);

        //serial port receive in its own thread
        private void sp_DataReceived(object sender, System.IO.Ports.SerialDataReceivedEventArgs e)
        {
            if (sp.IsOpen) 
            {
                try
                {
                    //give it a sec to spit it out
                    System.Threading.Thread.Sleep(50);

                    //read whatever is in port
                    string sentence = sp.ReadExisting();
                    this.BeginInvoke(new LineReceivedEventHandler(SerialLineReceived), sentence);
                }
                catch (Exception exc) 
                {
                    MessageBox.Show(exc.Message + "\n\r" + "\n\r" + "Go to Settings -> COM Ports to Fix", "Screwed!");
                }

            }

        }

        //Event Handlers
        //private void btnExit_Click(object sender, EventArgs e) { this.Exit(); }


        public void SerialPortOpenGPS()
        {
            //close it first
            SerialPortCloseGPS();

            if (!sp.IsOpen)
            {
                sp.PortName = portName;
                sp.BaudRate = baudRate;
                sp.DataReceived += sp_DataReceived;
                sp.WriteTimeout = 1000;
            }

            try { sp.Open(); }
            catch (Exception exc) 
            {
                MessageBox.Show(exc.Message + "\n\r" + "\n\r" + "Go to Settings -> COM Ports to Fix", "No Serial Port Active");

                //update port status labels
                stripPortGPS.Text = " * * ";
                stripPortGPS.ForeColor = Color.Red;
                stripOnlineGPS.Value = 1;

                //SettingsPageOpen(0);
            }

            if (sp.IsOpen)
            {
                //btnOpenSerial.Enabled = false;

                //discard any stuff in the buffers
                sp.DiscardOutBuffer();
                sp.DiscardInBuffer();

                //update port status label
                stripPortGPS.Text = portName + " " + baudRate.ToString();
                stripPortGPS.ForeColor = Color.ForestGreen;

                Properties.Settings.Default.setPort_portName = portName;
                Properties.Settings.Default.setPort_baudRate = baudRate;
                Properties.Settings.Default.Save();
            }
        }

        public void SerialPortCloseGPS()
        {
            //if (sp.IsOpen)
            {
                sp.DataReceived -= sp_DataReceived;
                try { sp.Close(); }
                catch (Exception exc) { MessageBox.Show(exc.Message, "Connection already terminated?"); }

                 //update port status labels
                stripPortGPS.Text = " * * " + baudRate.ToString();
                stripPortGPS.ForeColor = Color.ForestGreen;
                stripOnlineGPS.Value = 1;

                sp.Dispose();
            }

        }

         #endregion SerialPortGPS

    }//end class
}//end namespace