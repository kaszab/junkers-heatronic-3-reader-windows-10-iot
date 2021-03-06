﻿using HeatronicUwpLib.Dto;
using HeatronicUwpLib.Exceptions;
using HeatronicUwpLib.Extentions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Devices.SerialCommunication;
using Windows.Storage.Streams;

namespace HeatronicUwpLib
{
    public class NewMessageEventArgs : EventArgs
    {
        public HeatronicDTO Message { get; set; }
        public MessageType MessageType { get; set; }
    }

    public enum MessageType
    {
        Heater,
        Timestamp,
        HeaterCircuit,
        Warmwater
    }

    public delegate void NewMessageEventHandler(object sender, NewMessageEventArgs e);

    public class HeatronicGateway
    {
        private SerialDevice serialPort = null;
        private MessageDecoder decoder = new MessageDecoder();
        private CRCChecker crcChecker = new CRCChecker();

        public event NewMessageEventHandler NewMessage;

        private static HeatronicGateway _instance;
        private HeatronicGateway()
        {
            Task.Run(() =>
            {
                StartReadingAsync();
            });
        }

        protected virtual void OnNewMessage(NewMessageEventArgs e)
        {
            if (NewMessage != null)
                NewMessage(this, e);
        }

        private System.Threading.Timer timer;

        private void timerCallback(object state)
        {
            OnNewMessage(new NewMessageEventArgs()
            {
                Message =  new TimestampDTO()
                {
                    SystemTimestamp = new DateTime(2015, 1, 2, 3, 4, 5)
                },
                MessageType = MessageType.Timestamp
            });
            timer.Change((int)TimeSpan.FromSeconds(2).TotalMilliseconds, System.Threading.Timeout.Infinite);
        }

        private async void StartReadingAsync()
        {
            //if (true)
            //{
            //    timer = new System.Threading.Timer(timerCallback, null, (int)TimeSpan.FromSeconds(2).TotalMilliseconds, System.Threading.Timeout.Infinite);
            //    do { } while (true);
            //    return;
            //}

            string serialDeviceSelector = SerialDevice.GetDeviceSelector();
            var deviceList = await DeviceInformation.FindAllAsync(serialDeviceSelector);
            if (!deviceList.Any())
            {
                throw new HeatronicException("No serial device found.");
            }

            serialPort = await SerialDevice.FromIdAsync(deviceList.First().Id);
            if (serialPort == null)
            {
                throw new HeatronicException("Could not open serial device.");
            }

            serialPort.WriteTimeout = TimeSpan.FromMilliseconds(1000);
            serialPort.ReadTimeout = TimeSpan.FromMilliseconds(1000);
            serialPort.BaudRate = 9600;
            serialPort.Parity = SerialParity.None;
            serialPort.StopBits = SerialStopBitCount.One;
            serialPort.DataBits = 8;
            serialPort.Handshake = SerialHandshake.None;

            var dataReader = new DataReader(serialPort.InputStream);

            var loopInfinit = true;
            do
            {
                try
                {
                    await ReadDataAsync(dataReader); 
                }
                catch (Exception ex)
                {
                    Debug.Fail(ex.Message, ex.StackTrace);
                }
            } while (loopInfinit);

            dataReader.DetachStream();

        }


        private async Task ReadDataAsync(DataReader dataReader)
        {
            var singleByte = await dataReader.ReadByteAsync();
            var v1 = singleByte;
        Start:
            switch (singleByte)
            {
                case 0x88:
                    {
                        var v2 = singleByte = await dataReader.ReadByteAsync();
                        if (singleByte == 0x00) // An alle
                        {
                            var v3 = singleByte = await dataReader.ReadByteAsync();
                            switch (singleByte)
                            {
                                case 0x18: // Kessel-Telegramm: Heizgeraet
                                    await ReadHeizgerae(dataReader, v1, v2, v3);
                                    break;
                                case 0x19: // Kessel-Telegramm: Heizkreis 
                                    await ReadHeizkreis(dataReader, v1, v2, v3);
                                    break;
                                case 0x34: // Kessel-Telegramm: Warmwasser
                                    await ReadWarmwasser(dataReader, v1, v2, v3);
                                    break;
                                default:
                                    goto Start;
                            }
                        }
                        else
                        {
                            goto Start;
                        }
                    }
                    break;
                case 0x90: // FW100/FW200 Message
                    {
                        var v2 = singleByte = await dataReader.ReadByteAsync();
                        if (singleByte == 0x00) // An alle
                        {
                            var v3 = singleByte = await dataReader.ReadByteAsync();
                            switch (singleByte)
                            {
                                case 0x06: // Zeit
                                    await ReadTimestampAsync(dataReader, v1, v2, v3);
                                    break;
                                case 0xFF: // Kessel-Telegramm: Heizkreis Steuerung 
                                    {
                                        //var data = dataReader.ReadBytesAsync(12);
                                        //var message = decoder.BuildMessage(0x90, 0x00, 0xFF, data);
                                        //if (crcChecker.IsCrcOk(message))
                                        //{
                                        //    var dateTime = decoder.DecodeHeizkreisSteuerung(message);
                                        //    Debug.WriteLine("HeizkreisSteuerung Message: " + dateTime.ToString());
                                        //}
                                    }
                                    break;
                                default:
                                    goto Start;
                            }
                        }
                        else
                        {
                            goto Start;
                        }
                    }
                    break;
                case 0xA0: // Lastschaltmodul#1 (IPM)
                case 0xA1: // Lastschaltmodul#2 (IPM)
                case 0xB0: // Solarmodul (ISM)
                default:
                    break;
            }

        }

        private async Task ReadTimestampAsync(DataReader dataReader, byte v1, byte v2, byte v3)
        {
            var data = await dataReader.ReadBytesAsync(10);
            var message = decoder.BuildMessage(v1, v2, v3, data);
            if (crcChecker.IsCrcOk(message))
            {
                OnNewMessage(new NewMessageEventArgs()
                {
                    Message = decoder.DecodeDateTimeMessage(message),
                    MessageType = MessageType.Timestamp
                });
            }
        }

        private async Task ReadWarmwasser(DataReader dataReader, byte v1, byte v2, byte v3)
        {
            var data = await dataReader.ReadBytesAsync(19);
            var message = decoder.BuildMessage(v1, v2, v3, data);
            if (crcChecker.IsCrcOk(message))
            {
                OnNewMessage(new NewMessageEventArgs()
                {
                    Message = decoder.DecodeWarmwasser(message),
                    MessageType = MessageType.Warmwater
                });
            }
        }

        private async Task ReadHeizkreis(DataReader dataReader, byte v1, byte v2, byte v3)
        {
            var data = await dataReader.ReadBytesAsync(29);
            var message = decoder.BuildMessage(v1, v2, v3, data);
            if (crcChecker.IsCrcOk(message))
            {
                OnNewMessage(new NewMessageEventArgs()
                {
                    Message = decoder.DecodeHeizkreis(message),
                    MessageType = MessageType.HeaterCircuit
                });
            }
        }

        private async Task ReadHeizgerae(DataReader dataReader, byte v1, byte v2, byte v3)
        {
            var data = await dataReader.ReadBytesAsync(27);
            var message = decoder.BuildMessage(v1, v2, v3, data);
            if (crcChecker.IsCrcOk(message))
            {

                OnNewMessage(new NewMessageEventArgs()
                {
                    Message = decoder.DecodeHeizgeraet(message),
                    MessageType = MessageType.Heater
                });

            }
        }

        public static HeatronicGateway GetDefault()
        {
            if (_instance == null)
            {
                _instance = new HeatronicGateway();
            }
            return _instance;
        }
    }

}
