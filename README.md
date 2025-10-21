# AvaSitcpTMCM
DCS software for FASER AHCAL project based on Avalonia GUI.

# INSTALL
1. Copy the binary zip file, unzip it, and cd to ```cd AvaSitcpTMCM.v*```
2. Add the permission to ```AvaSitcpTMCM``` by ```chmod 755 AvaSitcpTMCM```

# Usage
## GUI mode 
  ```./AvaSitcpTMCM```
  - GUI for operation testing. Based on SitcpDAQ_TMCM by LiuHao.
  - some functions for sending the data to InfluxDB.
  - Test function for InfluxDB
  - the default value can be changed with modifying ```appsetting.json```
## CLI mode
  ```./AvaSitcpTMCM -cli```
  - CLI for operation.
  - Sending Temperature, current, and status to the InfluxDB.
  - Error handing with retrying from scrach, you can specify the number of retry with ```-r 10```.
  - the default value inside ```appsetting.json``` is used, IP address and port can changed via ```-ip 193.169.11.17 -port 25```.
## CLI test mode 
  ```./AvaSitcpTMCM -cli -test```
  - CLI for testing the TCP connection and InfluxDB connection.
  - I will add the testing for monitoring acquition later.
