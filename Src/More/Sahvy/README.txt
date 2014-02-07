About Sahvy
==========================================================
Sahvy (Sample-and-hold verifier) is a bounded symbolic 
model checker for discrete controllers (modeled as software) 
that sample and actuate continuous-time systems 
(modeled as ODEs). Sahvy verifies up-to a chosen time 
horizon.

Building Sahvy
==========================================================
In order to build Sahvy, you must first build the FORMULA
system. See the main README.txt for building FORMULA. Sahvy
requires additional libraries that are not part of this 
project and may be covered under different licenses. It is 
your responsibility to download, install, and agree to the 
licenses of these additional libraries.

1. Get the entire FORMULA source using git.
   a. Let $ be the root of the FORMULA repository on your machine.
   b. Follow the instructions in $\README.txt for building FORMULA.

2. Get OpenTK, for visualizing controller trajectories.
   a. Download this release of OpenTk: http://sourceforge.net/projects/opentk/files/opentk/opentk-1.1/rc-1/opentk-2014-01-15.zip/download
   b. Unzip OpenTK into Location
   c. Create folder OpenTK in $\Ext\OpenTK
   d. Copy from Location\Binaries -> $\Ext\OpenTK\Binaries
   e. Copy from Location\Dependencies -> $\Ext\OpenTK\Dependencies
  
3. Get .Net-compatible Flowstar libraries for computing continuous trajectories
   a. More instructions will follow.

Running Examples
==========================================================
Assuming sahvy was built in a directory called bin. 
From a command line run:

1. An adaptive cruise control example
   bin\sahvy.exe $\Src\More\Sahvy\Examples\ACC.4ml
2. A one water tank system without zeno
   bin\sahvy.exe $\Src\More\Sahvy\Examples\OneWatertank.4ml
3. A two water tank system with zeno behavior (suppressed by sampling)
   bin\sahvy.exe $\Src\More\Sahvy\Examples\TwoWatertanks.4ml
4. A closed-loop insulin pump controller
   bin\sahvy.exe $\Src\More\Sahvy\Examples\Diabetic.4ml
5. A test of taylor approximations of trigonometric functions.
   bin\sahvy.exe $\Src\More\Sahvy\Examples\sin.4ml
