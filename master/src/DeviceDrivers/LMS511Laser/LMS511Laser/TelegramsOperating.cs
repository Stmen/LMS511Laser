/* This file is part of *LMS511Laser*.
Copyright (C) 2015 Tiszai Istvan, tiszaii@hotmail.com

*program name* is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <http://www.gnu.org/licenses/>.*/

using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.ComponentModel;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;
using System.Reflection;
using System.Threading.Tasks;
using Brace.Shared.DeviceDrivers.LMS511Laser.Interfaces;
using Brace.Shared.DeviceDrivers.LMS511Laser.EventHandlers;
using Brace.Shared.DeviceDrivers.LMS511Laser.Helpers;
using Brace.Shared.DeviceDrivers.LMS511Laser.Commands;
using Brace.Shared.DeviceDrivers.LMS511Laser.Enums;
using Brace.Shared.DeviceDrivers.LMS511Laser.Counters;
using Brace.Shared.Diagnostics.Trace;


namespace Brace.Shared.DeviceDrivers.LMS511Laser
{
    /// <summary>
    /// TelegramsOperating  class
    /// </summary>
    /// <remarks>    
    /// Telegrams for Configuring and Operating the LMS1xx, LMS5xx.
    /// </remarks> 
    public class TelegramsOperating
    {       
        #region Variable private
        private IPAddress _IPAdress;
        private int _portNumber;
        private Socket _clientSocket;    
        private NetworkStream networkStream;
        private BackgroundWorker bwReceiver;
        private IPEndPoint _deviceEP;       
        private Semaphore _semaphor;       
        private byte[] _rbuffer;
        private byte[][] _rdatas;
        private TraceWrapper _traceWrapper;
        private readonly string[] _SopasErrorName = { "No error", "Wrong uselevel, access to method not allowed", "Try to access a variable with an unknown Sopas index",
                                                 "Try to access a variable with an unknown Sopas index", "4", "5", "6", "7", "8", "9", "10", "11", "12", "13","14","15", "16","17",
                                                    "18", "19", "20", "21", "22", "23", "24", "25", "26", "no defined error"};
        #endregion

        #region EventHandler part
        /// <summary>
        /// SCdevicestat responde event
        /// </summary>
        public event EventHandler<SCdevicestateEventArgs> SCdevicestate_CMD;
        /// <summary>
        /// Sopas_Error event
        /// </summary>
        public event EventHandler<SopasErrorEventArgs> Sopas_Error_CMD;
        /// <summary>
        /// DeviceIdent responde event
        /// </summary>       
        public event EventHandler<DeviceIdentEventArgs> DeviceIdent_CMD;
        /// <summary>
        /// Run responde event
        /// </summary> 
        public event EventHandler<RunEventArgs> Run_CMD;
        /// <summary>
        /// mDOSetOutput responde event
        /// </summary> 
        public event EventHandler<mDOSetOutputEventArgs> mDOSetOutput_CMD;
        /// <summary>
        /// LMDscandata coninue responde event
        /// </summary>        
        public event EventHandler<LMDscandataEventArgs> LMDscandata_CMD;
        /// <summary>
        /// LMDscandata start coninue responde event
        /// </summary>        
        public event EventHandler<LMDscandataEEventArgs> LMDscandataE_CMD;     
        /// <summary>
        /// SetAccessMode responde event
        /// </summary>   
        public event EventHandler<SetAccessModeEventArgs> SetAccessMode_CMD;     
        /// <summary>
        /// LCMstate responde event
        /// </summary>
        public event EventHandler<LCMstateEventArgs> LCMstate_CMD;
        /// <summary>
        /// LSPsetdatetimeEventArgs responde event
        /// </summary>
        public event EventHandler<LSPsetdatetimeEventArgs> LSPsetdatetime_CMD;
        /// <summary>
        /// LIDoutputstateEventArgs responde event
        /// </summary>
        public event EventHandler<LIDoutputstateEventArgs> LIDoutputstate_CMD;
        /// <summary>
        /// LIDrstoutpcnEventArgs responde event
        /// </summary>
        public event EventHandler<LIDrstoutpcnEventArgs> LIDrstoutpcn_CMD;
        /// <summary>
        ///mSCrebootEventArgs responde event
        /// </summary>
        public event EventHandler<mSCrebootEventArgs> mSCreboot_CMD;
        /// <summary>
        /// mLMPsetscancfgEventArgs responde event
        /// </summary>
        public event EventHandler<mLMPsetscancfgEventArgs> mLMPsetscancfg_CMD;
        /// <summary>
        /// LMDscandatacfgEventArgs responde event
        /// </summary>
        public event EventHandler<LMDscandatacfgEventArgs> LMDscandatacfg_CMD;
        #endregion

        #region Variable property
        public bool Connected
        {
            get
            {
                if ( this._clientSocket != null )
                    return this._clientSocket.Connected;
                else
                    return false;
            }
        }        
        #endregion

        #region Contsructors       
        public TelegramsOperating(string IP, int port, TraceWrapper traceWrapper)
        {
            _portNumber = port;
            _IPAdress = System.Net.IPAddress.Parse(IP);
            _traceWrapper = traceWrapper;
            _semaphor = new Semaphore(1, 1);
            _deviceEP = new IPEndPoint(_IPAdress, port);
        
            NetworkChange.NetworkAvailabilityChanged += new NetworkAvailabilityChangedEventHandler(NetworkChange_NetworkAvailabilityChanged);
            _rbuffer = new byte[4096];
            _traceWrapper.WriteInformation("Laser TelegramsOperating start.");
          //  _rbuffer = new byte[1570];  
        }
        #endregion

        #region Private Methods

        private void NetworkChange_NetworkAvailabilityChanged(object sender , NetworkAvailabilityEventArgs e)
        {
            if ( !e.IsAvailable )
            {
                OnNetworkDead(new MessageEventArgs("ERROR:Laser network is DEAD!"));
                OnDisconnectedFromServer(new MessageEventArgs("ERROR:Laser disconnected from server!"));
            }
            else
                OnNetworkAlived(new MessageEventArgs("Laser network is ALIVE!"));
        }

       private void StartReceive(object sender , DoWorkEventArgs e)
        {
            byte[] pattern;
           // int readLength = _rbuffer.Length;
            bool bScanStart = false;
           int readBytes;
           int ii = 0;
           byte[] rbuffer;
          
           try
           {         
            rbuffer =  new byte[1];
            while ( this._clientSocket.Connected )
            {
                for (ii = 0; ii < _rbuffer.Length; ii++)
                {
                    readBytes = networkStream.Read(rbuffer, 0, 1);
                    if (readBytes == 0)
                        break;
                    _rbuffer[ii] = rbuffer[0];
                    if (rbuffer[0] == 0x03)
                        break;
                }
                readBytes = ii;
                if (ii != 0)
                    readBytes++;                   
                if ( readBytes == 0 )
                    break;
               // DebugClass.DebugMessage("read bytes of length: ", readBytes);            
                String sTempr = BitConverter.ToString(_rbuffer, 0, readBytes).Replace("-", " ");
               // DebugClass.DebugMessage("read buffer0: ",FunctHelper.Print_byteArray_Hex(_rbuffer, readBytes));               
                if ((_rbuffer[0] != 0x02) || (_rbuffer[readBytes-1] != 0x03))
                    throw new Exception("ERROR Laser: telegram frame is bad (STX or ETX)!");

                ASCIIEncoding encoding = new ASCIIEncoding();
                byte[] truncArray = new byte[readBytes-1];
                Array.Copy(_rbuffer, truncArray, truncArray.Length);

               // String sTemp = BitConverter.ToString(truncArray, 0, truncArray.Length).Replace("-", " ");                
               // Console.WriteLine("read buffer: " + sTemp);
               
                _rdatas = SeparatedToByteArray(truncArray, 0x20);
                ii = 0;              
               /* foreach (byte[] data in _rdatas)
                {
                    Console.WriteLine("data " + ii.ToString() + ": " + FunctHelper.Print_byteArray_Hex_ASCII(data));
                    ii++;
                }*/
             /*   if (ii>0)
                {
                    if (_rdatas[ii - 1].Length>0)
                        _rdatas[ii - 1][_rdatas[ii - 1].Length - 1] = 0;
                }*/
                // ERROR : sFA                  
                pattern = encoding.GetBytes("sFA");
                if (BytePatternSearch(_rdatas[0], pattern, 0) >= 0)
                { // sFA
                    if (Sopas_Error_CMD != null)
                    {
                        int  iENumber;                      
                        byte[] bdata = FunctHelper.ASCIItoByte(_rdatas[1]);
                        iENumber = FunctHelper.ConvertToInt(bdata);
                        iENumber = (iENumber > 26) ? 27 : iENumber;

                        SopasErrorEventArgs s = new SopasErrorEventArgs(_SopasErrorName[iENumber]);
                        Sopas_Error_CMD(this, s);
                        continue;
                    }
                }
               
                // LMDscandata  
                pattern = encoding.GetBytes("LMDscandata");
                if (BytePatternSearch(_rdatas[1], pattern, 0) >= 0)
                {
                    pattern = encoding.GetBytes("sSN");
                    if (BytePatternSearch(_rdatas[0], pattern, 0) >= 0)
                    {
                        if (bScanStart)
                        {
                            bScanStart = false;                        
                        }                       
                        if (LMDscandata_CMD != null)
                        {
                            _rbuffer[readBytes - 1] = 0x0;
                            LMDscandata_R lmdScandata = new LMDscandata_R();
                            parser_rbuffer_to_LMDscandata_R(ref lmdScandata);
                            LMDscandataEventArgs s = new LMDscandataEventArgs(lmdScandata);
                            LMDscandata_CMD(this, s);
                            continue;
                        }
                    }
                    else
                    {                      
                        pattern = encoding.GetBytes("sRA");
                        if (BytePatternSearch(_rdatas[0], pattern, 0) >= 0)
                        {                            
                            if (LMDscandata_CMD != null)
                            {
                                _rbuffer[readBytes - 1] = 0x0;
                                LMDscandata_R lmdScandata = new LMDscandata_R();
                                parser_rbuffer_to_LMDscandata_R(ref lmdScandata);
                                LMDscandataEventArgs s = new LMDscandataEventArgs(lmdScandata);
                                LMDscandata_CMD(this, s);
                                continue;
                            }
                        }
                        else
                        {                         
                            pattern = encoding.GetBytes("sEA");
                            if (BytePatternSearch(_rdatas[0], pattern, 0) >= 0)
                            {                                                               
                                if (LMDscandataE_CMD != null)
                                {
                                    _rbuffer[readBytes - 1] = 0x0;                                                                    
                                    int measurement = (int)_rbuffer[17] - '0';                                    
                                    LMDscandataEEventArgs s = new LMDscandataEEventArgs(measurement);
                                    LMDscandataE_CMD(this, s);
                                    continue;
                                }
                            }
                            else
                                throw new Exception("ERROR Laser: rec:LMDscandata is bad!");
                        }
                    }
                }

                // Run 
                pattern = encoding.GetBytes("Run");
                if (BytePatternSearch(_rdatas[1], pattern, 0) >= 0)
                { // Run
                    pattern = encoding.GetBytes("sAN");
                    if (BytePatternSearch(_rdatas[0], pattern, 0) >= 0)
                    {                        
                        if (Run_CMD != null)
                        {
                            RunEventArgs s = new RunEventArgs((int)_rbuffer[9] - '0');                           
                            Run_CMD(this,s);
                            continue;
                        }
                    }
                    else
                        throw new Exception("ERROR Laser: rec:Run is bad!");
                }

                // SetAccessMode 
                pattern = encoding.GetBytes("SetAccessMode");
                if (BytePatternSearch(_rdatas[1], pattern, 0) >= 0)
                { //  SetAccessMode
                    pattern = encoding.GetBytes("sAN");
                    if (BytePatternSearch(_rdatas[0], pattern, 0) >= 0)
                    {                       
                        if (SetAccessMode_CMD != null)
                        {                          
                            SetAccessModeEventArgs s = new SetAccessModeEventArgs((int)_rdatas[2][0] - '0');
                            SetAccessMode_CMD(this,s);
                            continue;
                        }
                    }
                    else
                        throw new Exception("ERROR Laser: rec:SetAccessMode is bad!");
                }
                //LSPsetdatetime
                pattern = encoding.GetBytes("LSPsetdatetime");
                if (BytePatternSearch(_rdatas[1], pattern, 0) >= 0)
                { // LSPsetdatetime
                    pattern = encoding.GetBytes("sAN");
                    if (BytePatternSearch(_rdatas[0], pattern, 0) >= 0)
                    {
                        if (LSPsetdatetime_CMD != null)
                        {                                                    
                            LSPsetdatetimeEventArgs s = new LSPsetdatetimeEventArgs((int)_rdatas[2][0] - '0');
                            LSPsetdatetime_CMD(this, s);
                            continue;
                        }
                    }
                    else
                        throw new Exception("ERROR Laser: rec:LSPsetdatetime is bad!");
                }

                // mLMPsetscancfg
                pattern = encoding.GetBytes("mLMPsetscancfg");
                if (BytePatternSearch(_rdatas[1], pattern, 0) >= 0)
                { // mLMPsetscancfg
                    pattern = encoding.GetBytes("sAN");
                    if (BytePatternSearch(_rdatas[0], pattern, 0) >= 0)
                    {
                        if (mLMPsetscancfg_CMD != null)
                        {
                            _rbuffer[readBytes - 1] = 0x0;
                            mLMPsetscancfg_R sc = new mLMPsetscancfg_R();
                            parser_rbuffer_to_mLMPsetscancfg_R(ref sc);
                            mLMPsetscancfgEventArgs s = new mLMPsetscancfgEventArgs(sc);
                            mLMPsetscancfg_CMD(this, s);
                            continue;                         
                        }
                    }
                    else
                        throw new Exception("ERROR Laser: rec:mLMPsetscancfg is bad!");
                }

                // LMPscancfg
          /*      pattern = encoding.GetBytes("LMPscancfg");
                if (BytePatternSearch(_rdatas[1], pattern, 0) >= 0)
                { // Run
                    pattern = encoding.GetBytes("sRA");
                    if (BytePatternSearch(_rdatas[0], pattern, 0) >= 0)
                    {
                        if (LMPscancfg_CMD != null)
                        {
                            _rbuffer[readBytes - 1] = 0x0;
                            LMPscancfg_R s = new LMPscancfg_R();
                            parser_rbuffer_to_LMPscancfg_R(ref s);
                            LMPscancfg_CMD(s);
                            continue;
                        }
                    }
                    else
                        throw new Exception("ERROR Laser: rec:LMPscancfg is bad!");
                }*/

                // LMPoutputRange, set
       /*         pattern = encoding.GetBytes("LMPoutputRange");
                if (BytePatternSearch(_rdatas[1], pattern, 0) >= 0)
                { 
                    pattern = encoding.GetBytes("sWA");
                    if (BytePatternSearch(_rdatas[0], pattern, 0) >= 0)
                    { // LMPoutputRange, set
                        if (LMPoutputRange_CMD != null)
                        {
                            LMPoutputRange_R s = new LMPoutputRange_R();
                            s.statusCode = 1;
                            LMPoutputRange_CMD(s);
                            continue;
                        }
                    }
                    else
                    {
                        pattern = encoding.GetBytes("sRA");
                        if (BytePatternSearch(_rdatas[0], pattern, 0) >= 0)
                        { // LMPoutputRange, get
                            if (LMPoutputRange_get_CMD != null)
                            {
                                LMPoutputRange_get_R s = new LMPoutputRange_get_R();
                                parser_rbuffer_to_LMPoutputRange_R(ref s);
                                LMPoutputRange_get_CMD(s);
                                continue;
                            }
                        }
                        else
                            throw new Exception("ERROR Laser: rec:LMPoutputRange set or get is bad!");
                    }                    
                }*/
                // LCMstate
                pattern = encoding.GetBytes("LCMstate");
                if (BytePatternSearch(_rdatas[1], pattern, 0) >= 0)
                { // LCMstate
                    pattern = encoding.GetBytes("sRA");
                    if (BytePatternSearch(_rdatas[0], pattern, 0) >= 0)
                    {
                        if (LCMstate_CMD != null)
                        {                          
                            LCMstateEventArgs s = new LCMstateEventArgs((int)_rdatas[2][0] - '0'); 
                            LCMstate_CMD(this, s);
                            continue;
                        }
                    }
                    else
                        throw new Exception("ERROR Laser: rec:LMPscancfg is bad!");
                }
                // SCdevicestate 
                pattern = encoding.GetBytes("SCdevicestate");
                if (BytePatternSearch(_rdatas[1], pattern, 0) >= 0)
                { // SCdevicestate                   
                    pattern = encoding.GetBytes("sRA");
                    if (BytePatternSearch(_rdatas[0], pattern, 0) >= 0)
                    {                       
                        if (SCdevicestate_CMD != null)
                        {
                            SCdevicestateEventArgs s = new SCdevicestateEventArgs((int)_rbuffer[19] - '0');
                            SCdevicestate_CMD(this, s);
                            continue;
                        }
                    }
                    else
                        throw new Exception("ERROR Laser: rec:SCdevicestate is bad!");
                }
                // LIDoutputstate
                pattern = encoding.GetBytes("LIDoutputstate");
                if (BytePatternSearch(_rdatas[1], pattern, 0) >= 0)
                { // LIDoutputstate
                    pattern = encoding.GetBytes("sRA");
                    if (BytePatternSearch(_rdatas[0], pattern, 0) >= 0)
                    {
                        if (LIDoutputstate_CMD != null)
                        {                            
                            _rbuffer[readBytes - 1] = 0x0;
                            LIDoutputstate_R lidoutputstate = new LIDoutputstate_R();
                            parser_rbuffer_to_LIDoutputstate_R(ref lidoutputstate);
                            LIDoutputstateEventArgs s = new LIDoutputstateEventArgs(lidoutputstate);
                            LIDoutputstate_CMD(this, s);
                            continue;
                        }
                    }
                    else
                        throw new Exception("ERROR Laser: rec:LIDoutputstate is bad!");
                }
                // mDOSetOutput
                pattern = encoding.GetBytes("mDOSetOutput");
                if (BytePatternSearch(_rdatas[1], pattern, 0) >= 0)
                { // mDOSetOutput
                    pattern = encoding.GetBytes("sAN");
                    if (BytePatternSearch(_rdatas[0], pattern, 0) >= 0)
                    {
                        if (mDOSetOutput_CMD != null)
                        {                                                  
                            mDOSetOutputEventArgs s = new mDOSetOutputEventArgs((int)_rdatas[2][0] - '0');                            
                            mDOSetOutput_CMD(this, s);
                            continue;
                        }
                    }
                    else
                        throw new Exception("ERROR Laser: rec:mDOSetOutput is bad!");
                }
                // LIDrstoutpcnt
                pattern = encoding.GetBytes("LIDrstoutpcnt");
                if (BytePatternSearch(_rdatas[1], pattern, 0) >= 0)
                { // LIDrstoutpcnt
                    pattern = encoding.GetBytes("sAN");
                    if (BytePatternSearch(_rdatas[0], pattern, 0) >= 0)
                    {
                        if (LIDrstoutpcn_CMD != null)
                        {
                            LIDrstoutpcnEventArgs s = new LIDrstoutpcnEventArgs((int)_rdatas[2][0] - '0');
                            LIDrstoutpcn_CMD(this, s);
                            continue;
                        }
                    }
                    else
                        throw new Exception("ERROR Laser: rec:LMPscancfg is bad!");
                }
                // mSCreboot
                pattern = encoding.GetBytes("mSCreboot");
                if (BytePatternSearch(_rdatas[1], pattern, 0) >= 0)
                { // mSCreboot
                    pattern = encoding.GetBytes("sAN");
                    if (BytePatternSearch(_rdatas[0], pattern, 0) >= 0)
                    {
                        if (mSCreboot_CMD != null)
                        {
                            mSCrebootEventArgs s = new mSCrebootEventArgs(DateTime.Now);
                            mSCreboot_CMD(this, s);
                            continue;
                        }
                    }
                    else
                        throw new Exception("ERROR Laser: rec:LMPscancfg is bad!");
                }
                // DeviceIdent          
                pattern = encoding.GetBytes("sRA");
                if (BytePatternSearch(_rdatas[0], pattern, 0) >= 0)
                {                   
                    if (_rbuffer[5] == 0x30)
                    {                       
                        if (DeviceIdent_CMD != null)
                        {
                            _rbuffer[readBytes - 1] = 0x0;
                            DeviceIdentEventArgs s = new DeviceIdentEventArgs(Encoding.UTF8.GetString(_rbuffer, 7, readBytes - 7));
                            DeviceIdent_CMD(this, s);
                            continue;
                        }
                    }                   
                }
                // LMDscandatacfg
                pattern = encoding.GetBytes("LMDscandatacfg");
                if (BytePatternSearch(_rdatas[1], pattern, 0) >= 0)
                { // LMDscandatacfg
                    pattern = encoding.GetBytes("sWA");
                    if (BytePatternSearch(_rdatas[0], pattern, 0) >= 0)
                    {
                        if (LMDscandatacfg_CMD != null)
                        {
                            LMDscandatacfgEventArgs s = new LMDscandatacfgEventArgs(1);
                            LMDscandatacfg_CMD(this, s);
                            continue;
                        }
                    }
                    else
                        throw new Exception("ERROR Laser: rec:LMDscandatacfg is bad!");
                }

                // STlms, kesobb
              /*  pattern = encoding.GetBytes("STlms");
                if (BytePatternSearch(_rdatas[1], pattern, 0) >= 0)
                { // LCMstate
                    pattern = encoding.GetBytes("sRA");
                    if (BytePatternSearch(_rdatas[0], pattern, 0) >= 0)
                    {
                        if (STlms_CMD != null)
                        {
                            _rbuffer[readBytes - 1] = 0x0;
                            STlms_R stlms = new STlms_R();
                            parser_rbuffer_to_STlms_R(ref stlms);
                            STlmsEventArgs s = new STlmsEventArgs(stlms);
                            STlms_CMD(this, s);
                            continue;
                        }
                    }
                    else
                        throw new Exception("ERROR Laser: rec:LMPscancfg is bad!");
                }*/
                             
             //   OnCommandReceived(new CommandEventArgs(cmd));

            };
            }
           catch(Exception ex)
           {
               OnCommandReceivingFailed(new MessageEventArgs(ex.Message));
           }
            OnServerDisconnected(new ServerEventArgs(_clientSocket));
         //   Reconnecting();
        }

         private void parser_rbuffer_to_LMDscandata_R(ref LMDscandata_R lmdScandata)
         {
            byte[] bdata;
            int offset = 0;
            bdata = FunctHelper.ASCIItoByte(_rdatas[2]); 
            lmdScandata.versionNumber = FunctHelper.ConvertToUShort(bdata); //2
            bdata = FunctHelper.ASCIItoByte(_rdatas[3]);
            lmdScandata.deviceNumber = FunctHelper.ConvertToUShort(bdata); //3
            bdata = FunctHelper.ASCIItoByte(_rdatas[4]);
            lmdScandata.serialNumber = FunctHelper.ConvertToUint(bdata); //4 
            lmdScandata.deviceStatus = FunctHelper.ASCIItoByteOne(_rdatas[5][0]); //5 
            bdata = FunctHelper.ASCIItoByte(_rdatas[6]); 
            lmdScandata.telegramCounter = FunctHelper.ConvertToUShort(bdata); //6 
            bdata = FunctHelper.ASCIItoByte(_rdatas[7]);
            lmdScandata.scanCounter = FunctHelper.ConvertToUShort(bdata); //7
            bdata = FunctHelper.ASCIItoByte(_rdatas[8]);
            lmdScandata.timeSinceStartUp = FunctHelper.ConvertToUint(bdata);//8
            bdata = FunctHelper.ASCIItoByte(_rdatas[9]);
            lmdScandata.timeOfTransmission = FunctHelper.ConvertToUint(bdata); //9                   
            bdata = FunctHelper.ASCIItoByte(_rdatas[11]);
            lmdScandata.statusOfDigitalInputs = FunctHelper.ConvertToUShort(bdata); //11          
            bdata = FunctHelper.ASCIItoByte(_rdatas[13]);
            lmdScandata.statusOfDigitalOutputs = FunctHelper.ConvertToUShort(bdata); // 13                                  
            bdata = FunctHelper.ASCIItoByte(_rdatas[14]);
            lmdScandata.reserved = FunctHelper.ConvertToUShort(bdata); // 14
            bdata = FunctHelper.ASCIItoByte(_rdatas[16]);
            lmdScandata.scanFrequency = FunctHelper.ConvertToUint(bdata);//16           
            bdata = FunctHelper.ASCIItoByte(_rdatas[17]);
            lmdScandata.measurementFrequency = FunctHelper.ConvertToUint(bdata); //17         
            bdata = FunctHelper.ASCIItoByte(_rdatas[18]); //18
            lmdScandata.amountOfEncoder = FunctHelper.ConvertToUShort(bdata);
           
            if (lmdScandata.amountOfEncoder != 0)
            {
                bdata = FunctHelper.ASCIItoByte(_rdatas[19]);
                lmdScandata.encoderPosition = FunctHelper.ConvertToUShort(bdata);
                bdata = FunctHelper.ASCIItoByte(_rdatas[20]);
                lmdScandata.encoderSpeed = FunctHelper.ConvertToUShort(bdata);
                offset = 2;
            }
            bdata = FunctHelper.ASCIItoByte(_rdatas[19 + offset]);
            lmdScandata.amountOf16BitChannels = FunctHelper.ConvertToUShort(bdata);
           
            if (lmdScandata.amountOf16BitChannels > 0)
            {
                lmdScandata.content16 = Encoding.UTF8.GetString(_rdatas[20 + offset]);
                bdata = FunctHelper.ASCIItoByte(_rdatas[21 + offset]);
                lmdScandata.scaleFactor16 = FunctHelper.ConvertToFloat(bdata);
                bdata = FunctHelper.ASCIItoByte(_rdatas[22 + offset]);
                lmdScandata.scaleFactorOffset16 = FunctHelper.ConvertToFloat(bdata);
                bdata = FunctHelper.ASCIItoByte(_rdatas[23 + offset]);
                lmdScandata.startAngle16 = FunctHelper.ConvertToUint(bdata);
                bdata = FunctHelper.ASCIItoByte(_rdatas[24 + offset]);
                lmdScandata.steps16 = FunctHelper.ConvertToUShort(bdata);
                bdata = FunctHelper.ASCIItoByte(_rdatas[25 + offset]);
                lmdScandata.amountOfData16 = FunctHelper.ConvertToUShort(bdata);
                if (lmdScandata.amountOfData16 > 0)
                {
                    if (_rdatas.Length < (25 + offset + lmdScandata.amountOfData16))
                        throw new Exception("ERROR Laser: rec:LMDscandata data bad from amountOfData16 !");
                    lmdScandata.data16 = new ushort[lmdScandata.amountOfData16];
                    for (int ii = 0; ii < lmdScandata.amountOfData16; ii++, offset++)
                    {
                        bdata = FunctHelper.ASCIItoByte(_rdatas[26 + offset]);
                        lmdScandata.data16[ii] = FunctHelper.ConvertToUShort(bdata);
                    }
                }
                offset += 6;
            }
          
            bdata = FunctHelper.ASCIItoByte(_rdatas[20 + offset]);
            lmdScandata.amountOf8BitChannels = FunctHelper.ConvertToUShort(bdata);
            if (lmdScandata.amountOf8BitChannels > 0)
            {
                lmdScandata.content8 = Encoding.UTF8.GetString(_rdatas[21 + offset]);
                bdata = FunctHelper.ASCIItoByte(_rdatas[22 + offset]);
                lmdScandata.scaleFactor8 = FunctHelper.ConvertToFloat(bdata);
                bdata = FunctHelper.ASCIItoByte(_rdatas[23 + offset]);
                lmdScandata.scaleFactorOffset8 = FunctHelper.ConvertToFloat(bdata);
                bdata = FunctHelper.ASCIItoByte(_rdatas[24 + offset]);
                lmdScandata.startAngle8 = FunctHelper.ConvertToUint(bdata);
                bdata = FunctHelper.ASCIItoByte(_rdatas[25 + offset]);
                lmdScandata.steps8 = FunctHelper.ConvertToUShort(bdata);
                bdata = FunctHelper.ASCIItoByte(_rdatas[26 + offset]);
                lmdScandata.amountOfData8 = FunctHelper.ConvertToUShort(bdata);
                if (lmdScandata.amountOfData8 > 0)
                {
                    if (_rdatas.Length < (26 + offset + lmdScandata.amountOfData8))
                        throw new Exception("ERROR Laser: rec:LMDscandata data bad from amountOfData8 !");
                    lmdScandata.data16 = new ushort[lmdScandata.amountOfData8];
                    for (int ii = 0; ii < lmdScandata.amountOfData8; ii++, offset++)
                    {
                        bdata = FunctHelper.ASCIItoByte(_rdatas[27 + offset]);
                        lmdScandata.data8[ii] = bdata[0];
                    }
                }
                offset += 6;
            }           
            bdata = FunctHelper.ASCIItoByte(_rdatas[21 + offset]);
            lmdScandata.position = FunctHelper.ConvertToUShort(bdata);
            bdata = FunctHelper.ASCIItoByte(_rdatas[22 + offset]);
            lmdScandata.nameMode = FunctHelper.ConvertToUShort(bdata);
            if (lmdScandata.nameMode != 0)
            {
                if (_rdatas.Length < (23 + offset))
                    throw new Exception("ERROR Laser: rec:LMDscandata data bad from nameMode !");
                bdata = FunctHelper.ASCIItoByte(_rdatas[23 + offset]);
                lmdScandata.nameLength = bdata[0];
                lmdScandata.name = Encoding.UTF8.GetString(_rdatas[24 + offset]);
                offset += 2;
            }
            bdata = FunctHelper.ASCIItoByte(_rdatas[23 + offset]);
            lmdScandata.comment = FunctHelper.ConvertToUShort(bdata);
            bdata = FunctHelper.ASCIItoByte(_rdatas[24 + offset]);
            lmdScandata.timeMode = FunctHelper.ConvertToUShort(bdata);
            if (lmdScandata.timeMode != 0)
            {
                if (_rdatas.Length < (24 + offset))
                    throw new Exception("ERROR Laser: rec:LMDscandata data bad from timeMode !");
                bdata = FunctHelper.ASCIItoByte(_rdatas[25 + offset]);
                lmdScandata.timeYear = FunctHelper.ConvertToUShort(bdata);
                bdata = FunctHelper.ASCIItoByte(_rdatas[26 + offset]);
                lmdScandata.timeMonth = bdata[0];
                bdata = FunctHelper.ASCIItoByte(_rdatas[27 + offset]);
                lmdScandata.timeDay = bdata[0];
                bdata = FunctHelper.ASCIItoByte(_rdatas[28 + offset]);
                lmdScandata.timeHour = bdata[0];
                bdata = FunctHelper.ASCIItoByte(_rdatas[29 + offset]);
                lmdScandata.timeMinute = bdata[0];
                bdata = FunctHelper.ASCIItoByte(_rdatas[30 + offset]);
                lmdScandata.timeSecund = bdata[0];
                bdata = FunctHelper.ASCIItoByte(_rdatas[31 + offset]);
                lmdScandata.timeUsecund = FunctHelper.ConvertToUint(bdata);
            }
          //  Console.WriteLine("data 24 +  : " + offset + "-" + FunctHelper.Print_byteArray_Hex_ASCII(_rdatas[24 + offset]));
          //  Console.WriteLine(lmdScandata.timeMode.ToString());
         }

         private void parser_rbuffer_to_mLMPsetscancfg_R(ref mLMPsetscancfg_R data)
         {
             byte[] bdata;
             data.statusCode = (SetscancfgEnum)FunctHelper.ASCIItoByteOne(_rdatas[2][0]);
             bdata = FunctHelper.ASCIItoByte(_rdatas[3]);
             data.scan_frequency = FunctHelper.ConvertToUint(bdata);
             bdata = FunctHelper.ASCIItoByte(_rdatas[4]);
             data.value = FunctHelper.ConvertToShort(bdata);
             bdata = FunctHelper.ASCIItoByte(_rdatas[5]);
             data.angle_resolution = FunctHelper.ConvertToUint(bdata);
             bdata = FunctHelper.ASCIItoByte(_rdatas[6]);
             data.start_angle = FunctHelper.ConvertToInt(bdata);
             bdata = FunctHelper.ASCIItoByte(_rdatas[7]);
             data.stop_angle = FunctHelper.ConvertToInt(bdata);
         }

      /*   private void parser_rbuffer_to_LMPoutputRange_R(ref LMPoutputRange_get_R data)
         {
             byte[] bdata;
             data.statusCode = (int)FunctHelper.ASCIItoByteOne(_rdatas[2][0]);             
             bdata = FunctHelper.ASCIItoByte(_rdatas[3]);
             data.angle_resolution = FunctHelper.ConvertToUint(bdata);
             bdata = FunctHelper.ASCIItoByte(_rdatas[4]);
             data.start_angle = FunctHelper.ConvertToInt(bdata);
             bdata = FunctHelper.ASCIItoByte(_rdatas[5]);
             data.stop_angle = FunctHelper.ConvertToInt(bdata);
         }*/

         private void parser_rbuffer_to_LMPscancfg_R(ref LMPscancfg_R data)
         {
             byte[] bdata;
             bdata = FunctHelper.ASCIItoByte(_rdatas[2]);
             data.scan_frequency = FunctHelper.ConvertToUint(bdata);
             bdata = FunctHelper.ASCIItoByte(_rdatas[3]);
           //  data.value = FunctHelper.ConvertToShort(bdata);
             bdata = FunctHelper.ASCIItoByte(_rdatas[4]);
             data.angle_resolution = FunctHelper.ConvertToUint(bdata);
             bdata = FunctHelper.ASCIItoByte(_rdatas[5]);
             data.start_angle = FunctHelper.ConvertToInt(bdata);
             bdata = FunctHelper.ASCIItoByte(_rdatas[6]);
             data.stop_angle = FunctHelper.ConvertToInt(bdata);
         }

         private void parser_rbuffer_to_LIDoutputstate_R(ref LIDoutputstate_R data)
         {
             byte[] bdata;
             bdata = FunctHelper.ASCIItoByte(_rdatas[2]);
             data.statusCode = FunctHelper.ConvertToUint(bdata);             
             data.out1State = (byte)((int)_rdatas[4][0] - '0');
             bdata = FunctHelper.ASCIItoByte(_rdatas[5]);
             data.out1Count = FunctHelper.ConvertToUint(bdata);
             data.out2State = (byte)((int)_rdatas[6][0] - '0');
             bdata = FunctHelper.ASCIItoByte(_rdatas[7]);
             data.out2Count = FunctHelper.ConvertToUint(bdata);
             data.out3State = (byte)((int)_rdatas[8][0] - '0');
             bdata = FunctHelper.ASCIItoByte(_rdatas[9]);
             data.out3Count = FunctHelper.ConvertToUint(bdata);
             data.out4State = (byte)((int)_rdatas[10][0] - '0');
             bdata = FunctHelper.ASCIItoByte(_rdatas[11]);
             data.out4Count = FunctHelper.ConvertToUint(bdata);
             data.out5State = (byte)((int)_rdatas[12][0] - '0');
             bdata = FunctHelper.ASCIItoByte(_rdatas[13]);
             data.out5Count = FunctHelper.ConvertToUint(bdata);
             data.out6State = (byte)((int)_rdatas[14][0] - '0');
             bdata = FunctHelper.ASCIItoByte(_rdatas[15]);
             data.out6Count = FunctHelper.ConvertToUint(bdata);

             data.extOut1State = (byte)((int)_rdatas[16][0] - '0');
             bdata = FunctHelper.ASCIItoByte(_rdatas[17]);
             data.extOut1Count = FunctHelper.ConvertToUint(bdata);
             data.extOut2State = (byte)((int)_rdatas[18][0] - '0');
             bdata = FunctHelper.ASCIItoByte(_rdatas[19]);
             data.extOut2Count = FunctHelper.ConvertToUint(bdata);
             data.extOut3State = (byte)((int)_rdatas[20][0] - '0');
             bdata = FunctHelper.ASCIItoByte(_rdatas[21]);
             data.extOut3Count = FunctHelper.ConvertToUint(bdata);
             data.extOut4State = (byte)((int)_rdatas[22][0] - '0');
             bdata = FunctHelper.ASCIItoByte(_rdatas[23]);
             data.extOut4Count = FunctHelper.ConvertToUint(bdata);
             data.extOut5State = (byte)((int)_rdatas[24][0] - '0');
             bdata = FunctHelper.ASCIItoByte(_rdatas[25]);
             data.extOut5Count = FunctHelper.ConvertToUint(bdata);
             data.extOut6State = (byte)((int)_rdatas[26][0] - '0');
             bdata = FunctHelper.ASCIItoByte(_rdatas[27]);
             data.extOut6Count = FunctHelper.ConvertToUint(bdata);
             data.extOut7State = (byte)((int)_rdatas[28][0] - '0');
             bdata = FunctHelper.ASCIItoByte(_rdatas[29]);
             data.extOut7Count = FunctHelper.ConvertToUint(bdata);
             data.extOut8State = (byte)((int)_rdatas[30][0] - '0');
             bdata = FunctHelper.ASCIItoByte(_rdatas[31]);
             data.extOut8Count = FunctHelper.ConvertToUint(bdata);
         }

      /*   private void parser_rbuffer_to_STlms_R(ref STlms_R stlms)
         {
            
         }*/

         byte[][] SeparatedToByteArray(byte[] source, byte separator)
         {
             List<byte[]> Parts = new List<byte[]>();
             int Index = 0;
             byte[] Part;
             for (int i = 0; i < source.Length; ++i)
             {
                 if (source[i] == separator)
                 {
                     Part = new byte[i - Index];
                     Array.Copy(source, Index, Part, 0, Part.Length);
                     Parts.Add(Part);
                     Index = i + 1;
                    // i += separator.Length - 1;
                 }
             }
             Part = new byte[source.Length - Index];
             Array.Copy(source, Index, Part, 0, Part.Length);
             Parts.Add(Part);
             return Parts.ToArray();
         }

       void newConnecting(Object stateInfo)
       {
           ConnectToDevice();
       }

        private void bwSender_RunWorkerCompleted(object sender , RunWorkerCompletedEventArgs e)
        {
            if (!e.Cancelled && e.Error == null && ((bool)e.Result))
               // OnCommandSent(new MessageEventArgs("\nLaser sended!"));
                OnCommandSent(new MessageEventArgs(""));
            else
            {
                OnCommandSendingFailed(new MessageEventArgs("\nERROR: Laser not sended!"));
              //  Reconnecting();
            }

            ( (BackgroundWorker)sender ).Dispose();
            GC.Collect();
        }

        private void bwSender_DoWork(object sender , DoWorkEventArgs e)
        {
           // Command<T> cmd = (Command<T>)e.Argument;
            e.Result = SendCommandTo(e.Argument);
        }

        private Byte[] SerializeMessage<T>(T msg) where T : struct
        {
          //  int objsize = Marshal.SizeOf(typeof(T));
            int objsize = Marshal.SizeOf(msg);
            Byte[] ret = new Byte[objsize];
            IntPtr buff = Marshal.AllocHGlobal(objsize);
            Marshal.StructureToPtr(msg, buff, true);
            Marshal.Copy(buff, ret, 0, objsize);
            Marshal.FreeHGlobal(buff);
            return ret;
        }

        private T DeserializeMsg<T>(Byte[] data) where T : struct
        {
            int objsize = Marshal.SizeOf(typeof(T));
            IntPtr buff = Marshal.AllocHGlobal(objsize);
            Marshal.Copy(data, 0, buff, objsize);
            T retStruct = (T)Marshal.PtrToStructure(buff, typeof(T));
            Marshal.FreeHGlobal(buff);
            return retStruct;
        }

        private int BytePatternSearch(byte[] searchIn, byte[] searchBytes, int start = 0)
        {
            int found = -1;
            bool matched = false;
            //only look at this if we have a populated search array and search bytes with a sensible start
            if (searchIn.Length > 0 && searchBytes.Length > 0 && start <= (searchIn.Length - searchBytes.Length) && searchIn.Length >= searchBytes.Length)
            {
                //iterate through the array to be searched
                for (int i = start; i <= searchIn.Length - searchBytes.Length; i++)
                {
                    //if the start bytes match we will start comparing all other bytes
                    if (searchIn[i] == searchBytes[0])
                    {
                        if (searchIn.Length > 1)
                        {
                            //multiple bytes to be searched we have to compare byte by byte
                            matched = true;
                            for (int y = 1; y <= searchBytes.Length - 1; y++)
                            {
                                if (searchIn[i + y] != searchBytes[y])
                                {
                                    matched = false;
                                    break;
                                }
                            }
                            //everything matched up
                            if (matched)
                            {
                                found = i;
                                break;
                            }

                        }
                        else
                        {
                            //search byte is only one bit nothing else to do
                            found = i;
                            break; //stop the loop
                        }

                    }
                }

            }
            return found;
        }
        
       
        private bool SendCommandTo(Object cmd)
        {
            try
            { 
                byte[] sbuffer = null;
                CommandType type = CommandType.None;
                PropertyInfo[] propertyInfos = cmd.GetType().GetProperties();          
                foreach (PropertyInfo propertyInfo in propertyInfos)
                {     
                    if(propertyInfo.PropertyType == typeof(CommandType))
                    {
                        type =  (CommandType)propertyInfo.GetValue(cmd, null);               
                    }
                    if(propertyInfo.PropertyType == typeof(SCdevicestate))
                    {                                       
                        sbuffer = SerializeMessage<SCdevicestate>((SCdevicestate)propertyInfo.GetValue(cmd, null));
                    }
                    else
                    if (propertyInfo.PropertyType == typeof(DeviceIdent))
                    {
                        sbuffer = SerializeMessage<DeviceIdent>((DeviceIdent)propertyInfo.GetValue(cmd, null));
                        break;
                    }
                    else
                    if ((propertyInfo.PropertyType == typeof(LMDscandata)) && (type == CommandType.LMDscandata))
                    {                       
                        sbuffer = SerializeMessage<LMDscandata>((LMDscandata)propertyInfo.GetValue(cmd, null));
                        break;
                    }
                    else
                    if ((propertyInfo.PropertyType == typeof(LMDscandata_E)) && (type == CommandType.LMDscandata_E))
                    {
                        sbuffer = SerializeMessage<LMDscandata_E>((LMDscandata_E)propertyInfo.GetValue(cmd, null));
                        break;
                    }
                    else
                    if ((propertyInfo.PropertyType == typeof(Run)) && (type == CommandType.Run))
                    {
                        sbuffer = SerializeMessage<Run>((Run)propertyInfo.GetValue(cmd, null));
                        break;
                    }
                    else
                    if ((propertyInfo.PropertyType == typeof(SetAccessMode)) && (type == CommandType.SetAccessMode))
                    {
                        sbuffer = SerializeMessage<SetAccessMode>((SetAccessMode)propertyInfo.GetValue(cmd, null));
                        break;
                    }
                    else
                    if ((propertyInfo.PropertyType == typeof(mLMPsetscancfg)) && (type == CommandType.mLMPsetscancfg))
                    {
                        sbuffer = SerializeMessage<mLMPsetscancfg>((mLMPsetscancfg)propertyInfo.GetValue(cmd, null));
                        break;
                    }
                    else
                    if ((propertyInfo.PropertyType == typeof(LMPscancfg)) && (type == CommandType.LMPscancfg))
                    {
                        sbuffer = SerializeMessage<LMPscancfg>((LMPscancfg)propertyInfo.GetValue(cmd, null));
                        break;
                    }
                    else
                        if ((propertyInfo.PropertyType == typeof(mDOSetOutput)) && (type == CommandType.mDOSetOutput))
                    {
                        sbuffer = SerializeMessage<mDOSetOutput>((mDOSetOutput)propertyInfo.GetValue(cmd, null));
                        break;
                    }
                    else
                    if ((propertyInfo.PropertyType == typeof(LCMstate)) && (type == CommandType.LCMstate))
                    {
                        sbuffer = SerializeMessage<LCMstate>((LCMstate)propertyInfo.GetValue(cmd, null));
                        break;
                    }
                    else
                    if ((propertyInfo.PropertyType == typeof(STlms)) && (type == CommandType.STlms))
                    {
                        sbuffer = SerializeMessage<STlms>((STlms)propertyInfo.GetValue(cmd, null));
                        break;
                    }
                    else
                    if ((propertyInfo.PropertyType == typeof(LSPsetdatetime)) && (type == CommandType.LSPsetdatetime))
                    {
                        sbuffer = SerializeMessage<LSPsetdatetime>((LSPsetdatetime)propertyInfo.GetValue(cmd, null));
                        break;
                    }
                    else
                    if ((propertyInfo.PropertyType == typeof(LIDoutputstate)) && (type == CommandType.LIDoutputstate))
                    {
                        sbuffer = SerializeMessage<LIDoutputstate>((LIDoutputstate)propertyInfo.GetValue(cmd, null));
                        break;
                    }
                    else
                    if ((propertyInfo.PropertyType == typeof(LIDrstoutpcnt)) && (type == CommandType.LIDrstoutpcnt))
                    {
                        sbuffer = SerializeMessage<LIDrstoutpcnt>((LIDrstoutpcnt)propertyInfo.GetValue(cmd, null));
                        break;
                    }
                    else
                    if ((propertyInfo.PropertyType == typeof(mSCreboot)) && (type == CommandType.mSCreboot))
                    {
                        sbuffer = SerializeMessage<mSCreboot>((mSCreboot)propertyInfo.GetValue(cmd, null));
                        break;
                    }
                    else
                    if ((propertyInfo.PropertyType == typeof(mLMPsetscancfg)) && (type == CommandType.mLMPsetscancfg))
                    {
                        sbuffer = SerializeMessage<mLMPsetscancfg>((mLMPsetscancfg)propertyInfo.GetValue(cmd, null));
                        break;
                    }
                    else
                    if ((propertyInfo.PropertyType == typeof(LMDscandatacfg)) && (type == CommandType.LMDscandatacfg))
                    {
                        sbuffer = SerializeMessage<LMDscandatacfg>((LMDscandatacfg)propertyInfo.GetValue(cmd, null));
                        break;
                    }
                    /* if ((propertyInfo.PropertyType == typeof(LMPoutputRange)) && (type == CommandType.LMPoutputRange))
                     {
                         sbuffer = SerializeMessage<LMPoutputRange>((LMPoutputRange)propertyInfo.GetValue(cmd, null));
                         break;
                     }
                     else
                     if ((propertyInfo.PropertyType == typeof(LMPoutputRange_get)) && (type == CommandType.LMPoutputRange_get))
                     {
                         sbuffer = SerializeMessage<LMPoutputRange_get>((LMPoutputRange_get)propertyInfo.GetValue(cmd, null));
                         break;
                     }
                     else*/        
                }
              //  Console.WriteLine("send buffer: " + FunctHelper.Print_byteArray_Hex_ASCII(sbuffer));
                _traceWrapper.WriteInformation("TelegramsOperating: send buffer: " + FunctHelper.Print_byteArray_Hex_ASCII(sbuffer));
                _semaphor.WaitOne();
                networkStream.Write(sbuffer, 0, sbuffer.Length);
                networkStream.Flush();
                _semaphor.Release();       
                return true;
            }
            catch (Exception ex)
            { //
               
                OnCommandSendingFailed(new MessageEventArgs("ERROR:Laser sended error: " + ex.Message));
                _semaphor.Release();
                return false;
            }
        }
        #endregion

        #region Public Methods       
        public void ConnectToDevice()
        {
            BackgroundWorker bwConnector = new BackgroundWorker();
            bwConnector.DoWork += new DoWorkEventHandler(bwConnector_DoWork);
            bwConnector.RunWorkerCompleted += new RunWorkerCompletedEventHandler(bwConnector_RunWorkerCompleted);
            bwConnector.RunWorkerAsync();
        }
        public void Reconnecting()
        {
            Disconnect();
            Timer T = new Timer(new TimerCallback(newConnecting), null, 2000, 0);
        }
        private void bwConnector_RunWorkerCompleted(object sender , RunWorkerCompletedEventArgs e)
        {
            if (!((bool)e.Result))
            {
               // OnConnectingFailed(new MessageEventArgs("ERROR:Laser connected failer!"));
              //  Reconnecting();
            }
            else
                OnConnectingSuccessed(new MessageEventArgs("Laser connected"));

            ( (BackgroundWorker)sender ).Dispose();
        }


        private void bwConnector_DoWork(object sender , DoWorkEventArgs e)
        {
            try
            {
                CancellationToken cancellationToken;
                cancellationToken.ThrowIfCancellationRequested();
                this._clientSocket = new Socket(AddressFamily.InterNetwork , SocketType.Stream , ProtocolType.Tcp);
                //this._clientSocket.Connect(_deviceEP);

                IAsyncResult result = this._clientSocket.BeginConnect(_IPAdress, _portNumber, null, null);

                bool success = result.AsyncWaitHandle.WaitOne(5000, true);

                if (!success)
                {                   
                    this._clientSocket.Close();
                    e.Result = false;
                    OnConnectingFailed(new MessageEventArgs("ERROR:Laser connected failer 0!"));
                 //   throw new OperationCanceledException("ERROR:Failed to connect server.");
                    return;
                }
            } 
            catch (OperationCanceledException ex) 
            {
				 this._clientSocket.Dispose ();
				 this._clientSocket = null;
                 e.Result = false;
                 OnConnectingFailed(new MessageEventArgs(ex.Message));
                 return;
		    } 
            catch 
            {
                 this._clientSocket.Close();
				 this._clientSocket.Dispose ();
				 this._clientSocket = null;
                 e.Result = false;
                 OnConnectingFailed(new MessageEventArgs("ERROR:Laser connected failer 1 !"));
                 return;
			}
            try
            {
                e.Result = true;
                networkStream = new NetworkStream(this._clientSocket);
                this.bwReceiver = new BackgroundWorker();
                this.bwReceiver.WorkerSupportsCancellation = true;
                this.bwReceiver.DoWork += new DoWorkEventHandler(StartReceive);
                this.bwReceiver.RunWorkerAsync();
                //List<string> data = null;           
               // Command status = new Command(CommandType.sRN, "SCdevicestate", null);
               // SendCommandTo(status);
            }
            catch
            {
                e.Result = false;
                OnConnectingFailed(new MessageEventArgs("ERROR:Laser connected failer 2 !"));
            }
        }
        
        // Sends a command to the server if the connection is alive.       
        public void SendCommand(Object cmd)
        {
            if ( (_clientSocket != null) && _clientSocket.Connected )
            {
                new Timer(new TimerCallback(on_sendTimeout), null, 2000, 0);
                BackgroundWorker bwSender = new BackgroundWorker();
                bwSender.DoWork += new DoWorkEventHandler(bwSender_DoWork);
                bwSender.RunWorkerCompleted += new RunWorkerCompletedEventHandler(bwSender_RunWorkerCompleted);
                bwSender.WorkerSupportsCancellation = true;
                bwSender.RunWorkerAsync(cmd);
            }
            else
                OnCommandSendingFailed(new MessageEventArgs("ERROR:Laser not sended"));
        }

        void on_sendTimeout(Object stateInfo)
        {
            OnCommandSendingFailed(new MessageEventArgs("ERROR:Laser sended timeout"));
        }
        
        
        // Disconnect the client from the server and returns true if the client had been disconnected from the server.        
        public bool Disconnect()
        {
            if (this._clientSocket != null && this._clientSocket.Connected )
            {
                try
                {
                    _clientSocket.Shutdown(SocketShutdown.Both);
                    _clientSocket.Close();
                    bwReceiver.CancelAsync();
                    OnDisconnectedFromServer(new MessageEventArgs("Laser disconnected"));
                    return true;
                }
                catch
                {
                    return false;
                }
            }
            else
                return true;
        } 
        #endregion

        #region Events        
        // Occurs when a command received from a remote client.        
        public event CommandReceivedEventHandler CommandReceived;
        protected virtual void OnCommandReceived(CommandEventArgs e)
        {
            if ( CommandReceived != null )
            {
                 CommandReceived(this , e);
            }
        }

        // Occurs when a command sending action had been failed.This is because disconnection or sending exception.       
        public event CommandReceivingFailedEventHandler CommandReceivingFailed;
        protected virtual void OnCommandReceivingFailed(MessageEventArgs e)
        {
            if (CommandReceivingFailed != null)
            {
                CommandReceivingFailed(this, e);
            }
        }

        // Occurs when a command had been sent to the the remote server Successfully.        
        public event CommandSentEventHandler CommandSent;
        protected virtual void OnCommandSent(MessageEventArgs e)
        {
            if ( CommandSent != null )
            {
                CommandSent(this , e);
            }
        }
       
        // Occurs when a command sending action had been failed.This is because disconnection or sending exception.       
        public event CommandSendingFailedEventHandler CommandSendingFailed;
        protected virtual void OnCommandSendingFailed(MessageEventArgs e)
        {
            if (CommandSendingFailed != null)
            {
                CommandSendingFailed(this, e);
            }
        }
       
        // Occurs when the client disconnected.  
        public event ServerDisconnectedEventHandler ServerDisconnected;      
        protected virtual void OnServerDisconnected(ServerEventArgs e)
        {
            if ( ServerDisconnected != null )
            {
                ServerDisconnected(this , e);
            }
        }

        // Occurs when this client disconnected from the remote server.       
        public event DisconnectedEventHandler DisconnectedFromServer;
        protected virtual void OnDisconnectedFromServer(MessageEventArgs e)
        {
            if ( DisconnectedFromServer != null )
            {
                DisconnectedFromServer(this , e);
            }
        }
       
        // Occurs when this client connected to the remote server Successfully.        
        public event ConnectingSuccessedEventHandler ConnectingSuccessed;
        protected virtual void OnConnectingSuccessed(MessageEventArgs e)
        {
            if ( ConnectingSuccessed != null )
            {
                ConnectingSuccessed(this , e);
            }
        }
        
        // Occurs when this client failed on connecting to server.        
        public event ConnectingFailedEventHandler ConnectingFailed;
        protected virtual void OnConnectingFailed(MessageEventArgs e)
        {
            if ( ConnectingFailed != null )
            {
                ConnectingFailed(this , e);
            }
        }
       
        // Occurs when the network had been failed.      
        public event NetworkDeadEventHandler NetworkDead;
        protected virtual void OnNetworkDead(MessageEventArgs e)
        {
            if ( NetworkDead != null )
            {          
                NetworkDead(this , e);
            }
        }
        
        // Occurs when the network had been started to work.        
        public event NetworkAlivedEventHandler NetworkAlived;
        protected virtual void OnNetworkAlived(MessageEventArgs e)
        {
            if ( NetworkAlived != null )
            {
                NetworkAlived(this , e);
            }
        }
        #endregion
    }
}
