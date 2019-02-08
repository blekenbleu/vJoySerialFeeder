# VJoySerialFeeder #

## What is it? ##
A fork of [vJoySerialFeeder](https://github.com/Cleric-K/vJoySerialFeeder) for feeding data from a serial port to a virtual joystick.  Currently supported:
* [vJoy](http://vjoystick.sourceforge.net) (2.x and later, Windows)
* [vXbox](https://github.com/shauleiz/ScpVBus/releases) (Windows). [How-to](Docs/vXbox.md).
* [uinput](https://www.kernel.org/doc/html/v4.12/input/uinput.html) (Linux). [How-to](Docs/Linux.md).

Serial port data protocols currently supported:
* [IBUS](https://github.com/qba667/MAVLinkToIbus/blob/master/MAVLinkToIBUS/IBUSTelemetry.h) - used by FlySky radio controllers.
* SBUS - used by FrSky, Futaba radio controllers.
* MultiWii Serial Protocol - used by RC Flight Controllers running MultiWii, CleanFlight, BetaFlight, iNav, etc.
* KISS serial protocol - used by KISS RC Flight Controllers.
* PPM (converted to IBUS) - see [Use case 8](#use-cases).

Received data can be _mapped_ to any virtual joystick axis or button in very flexible and configurable way.

![Screenshot](Docs/images/screenshot.png)

## Use cases ##
1. Use Arduino to read data from _any_ device and send it to your PC - basic sketch in the [Arduino](Arduino/Joystick) directory.  
   See [example](Docs/Arduino.md) on using old RC controller for simulators.
2. Read RC controller (FlySky) directly from any [IBUS](https://github.com/betaflight/betaflight/wiki/Single-wire-FlySky-(IBus)-telemetry) capable receiver. [How-to](Docs/FlySky.md).
3. Read RC controller (FrSky, Futaba, etc.) directly from any SBUS receiver. [How-to](Docs/Sbus.md).
4. Use [MultiWii](http://www.multiwii.com/wiki/) compatible Flight Controller (MultiWii, CleanFlight, BetaFlight, etc.).  
   You can use your actual RC model. [How-to](Docs/MultiWii.md).
5. Use KISS Flight Controller. You can use your actual RC model.  
   It is pretty much the same as MultiWii, except using [KISS](https://kiss.flyduino.net/knowledge-base-category/kiss-fc-kb/) protocol.
6. Use as _general_ virtual joystick feeder. Although the program has the word "Serial" in its name,  
   it is not strictly necessary to feed the virtual joystick from the serial port.  
   You can _interact_ with vJoySerialFeeder in different ways and feed your own data to the virtual joystick   
   _or_ you can get the serial data and use it for your own purposes other than controlling a virtual joystick. [How-to](Docs/Interaction.md).
7. Feed over network. Use pairs of virtual serial ports provided by [com0com](http://com0com.sourceforge.net/) and [com2tcp](https://sourceforge.net/projects/com0com/files/com2tcp) for TCP/IP transport.  
   Another option is [HW VSP3](https://www.hw-group.com/products/hw_vsp/index_en.html), which combines the virtual serial port and the TCP/IP transport,  
   but the free version allows only one COM port.
8. If you have older RC receiver that only supports PPM you can use Arduino and [this sketch](https://github.com/wdcossey/ppm-to-ibus-serial) to convert PPM -> IBUS.  
   Thanks to [wdcossey](https://github.com/wdcossey/)!

## How to get it? ##
Download binaries from [Cleric-K releases](https://github.com/Cleric-K/releases) or build it yourself.  
Development here uses Visual Studio Commmunity 2017 V 15.9.4

## How to use it? ##
Check out the [Manual](Docs/README.md).

## Why this fork? ##
Updating to use [ViGEm](https://github.com/ViGEm) instead of vJoy or vXBox, neither of which are UWD, as required by latest Windows 10..  
More details [here](https://blekenbleu.github.io/Arduino/VJoySerialFeeder)

