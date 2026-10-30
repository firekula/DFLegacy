# Measurement corrections and scope

- v1 baseline-active-C3ED exceeded the existing readonly Process helper14000-call budget after two complete phases; later stages incomplete. Retained privately and never used as complete acceptance. v2 closes/reopens the exact verified process per bounded phase.
- v2/v3 delta-one-only interval summaries exclude counter resets/multiple increments. They cannot support a blanket no-stutter claim. B5DD34 resets in original4336D8 and increments in43382F; B5DD20 advances per FPS window. v4/v5 retain every read start/end and compare the pair, including resets.
- v4's raw scope string inherited "No screenshot in measured phases" from an older collector and is incorrect for its explicit screenshots phase. The actual phase, input events containing six vk44 operations and six original JPG file records are authoritative. Raw bytes remain unchanged. v5 has no vk44 and image_before/after are exactly equal.
- Only contiguous successful reads (inter-read <=10ms, read duration <=5ms) establish a constant-pair lower bound. Observer scheduling gaps are excluded, not interpreted as game freezes. v5 short includes one62.22ms observer gap; no claim is made about game behavior inside that missing interval.
- Original timer inactivity is not GPU Present/frame latency. Six screenshot pauses are demonstrated; all other intermittent stutters are not declared fixed.
- WPR GeneralProfile.Light failed Windows profile-policy permission0xc5585011 before recording. No policy was changed and later status was not recording. No ETW stack attribution is claimed.
- First focused Python invocation lacked PYTHONPATH=tools and failed collection; preserved separately. Corrected invocation and complete suite results are separate logs.
