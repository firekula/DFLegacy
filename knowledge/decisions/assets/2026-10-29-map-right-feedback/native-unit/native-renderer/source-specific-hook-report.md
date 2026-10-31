# Source-specific hook correction

Root's first liveF5 run (oldc63ee94/sourceF5) accepted a right-click target andautowalk,but feedback hadzero texture creations andzero draw calls. Only accepted_goal→expiry diagnostics were observed. Preserve that run aslive renderingRED. This subtask performed no live/client operation.

Independent offline PE confirmation now identifies the correct owner callback:881860 selects its visible panel-root child,thenECX=minimap owner at88187C calls8814A0 at88187E,followed by880B90 at881885. Original bytes E81DFCFFFF;8814A0 is949bytes,one plainRET,no incoming positive EBP references,no caller stack pushes. Complete raw/asm/hash contract is in ../../native-contract-increment/ (absolute path listed below).

The assigned renderer files now use DrawCallAddress88187E,DrawTargetAddress8814A0 andOriginalCallE81DFCFFFF. The preserved original-first bridge still calls once andreturns fullEAX. AfterDraw matches self tobinding.minimapToken. ExistingReadCanvasroot/plane/recheck,unique player template,private texture/native draw,state restoration,P2 drain andtwo-observation cleanup remain intact. Pin signatures now cover8814A0 entry,881860 callback entry,and88187C context including theunchanged following880B90 call. No last_callback_self diagnostic was needed.

Before changing this behavior,SourceSpecificBinding tests were added to the actualAfterDraw path withfakeReader memory andmissing native signatures. Three valid runtime assertions failed:oldhookmetadata,owner selfnotreaching protected path,andpanel root incorrectly accepted. SeeSourceSpecific-red-build.log/SourceSpecific-red.log(exit3). After correction,SourceSpecific-green-build.log/SourceSpecific-green.log show/W4/WX buildwithout warnings andalltests pass(exit0). The synthetic owner callback proves binding/branch selection by observing native_signature_unavailable andcancelled feedback; the genericroot call preserves the active feedback andpriorreason. Neither callback executes any genuine absolute game function.

New build outputs are inSourceSpecific-red-obj/SourceSpecific-green-obj andnew executables; priorRED/GREEN,P2evidence,oldsource-review andoldnative-contract were not overwritten. Manifest:native-renderer-source-specific-manifest.json.

No claim of actual GPU/native rendering success is made. First real private allocation/lock/draw,source-specific callback invocation,visible center/two yellow pulses,unchanged player blue pulse andcleanup/device behavior must be confirmed byroot after fresh loading. New source is ready forindependent review/build,thenroot-owned live acceptance.

Absolute increment directory:G:\Game\DNF_ACT1\DFLegacy\.tmp\map-right-pulse-20261029\native-contract-increment.
