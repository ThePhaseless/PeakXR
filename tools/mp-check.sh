#!/usr/bin/env bash
# Compare the two-client multiplayer test logs (docs/testing.md, "Two-client multiplayer test") and print one
# PASS/FAIL/SKIP line per check. Exit status 1 when any check fails.
#   tools/mp-check.sh [--modded-b] [mp-dir]   (default: $HOME/.var/app/com.valvesoftware.Steam/peakxr-mp)
# Tolerances: POS_TOL (m, default 1.0) for the player's position, LAND_TOL (m, default 1.5) for dropped/thrown items
# and the ping point, LAG_TOL (s, default 1.0) for B to receive what A did after the checkpoint, HOLD (s, default 2.0,
# A's CheckpointHold) for how late B may answer a checkpoint while A still stands still.
# --modded-b: B ran PeakXR flat (docs/testing.md, "Modded B"). Instead of "no event 151 anywhere" it checks B's
# handshake, the 151 stream from A (mean rate over the seconds with packets between RATE_MIN and RATE_MAX per second,
# defaults 2 and 30), A's avatar hands on B within HAND_TOL (m, default 0.25) of A's tracked hands, that B sent no 151,
# ran flat and logged no mod errors. Every other check is the same as the default (vanilla B) mode.
set -euo pipefail

MODDED=false
if [[ "${1:-}" == "--modded-b" ]]; then
    MODDED=true
    shift
fi
DIR="${1:-$HOME/.var/app/com.valvesoftware.Steam/peakxr-mp}"
POS_TOL="${POS_TOL:-1.0}"
LAND_TOL="${LAND_TOL:-1.5}"
LAG_TOL="${LAG_TOL:-1.0}"
HOLD="${HOLD:-2.0}"
HAND_TOL="${HAND_TOL:-0.25}"
RATE_MIN="${RATE_MIN:-2}"
RATE_MAX="${RATE_MAX:-30}"
A="$DIR/a-checkpoints.jsonl"
B="$DIR/b-observer.jsonl"
SUM="$DIR/b-summary.json"
DONE="$DIR/a-done.json"

for f in "$A" "$B" "$SUM"; do
    [[ -s "$f" ]] || { echo "FAIL missing $f"; exit 1; }
done

checks() {
# A's checkpoints, each joined with B's checkpoint of the same name, become one result line per check. What B
# received for a step is read from B's own rpc lines in the step's time window, not the checkpoint's capped list:
# "seen" runs from B's previous checkpoint to this one plus LAG_TOL (an RPC can land just after B's snapshot), and
# "after" runs on to B's next checkpoint, for the checks that something must never arrive.
jq -nr --slurpfile a "$A" --slurpfile b "$B" --slurpfile s "$SUM" \
    --argjson pos "$POS_TOL" --argjson land "$LAND_TOL" --argjson lag "$LAG_TOL" --argjson hold "$HOLD" \
    --argjson modded "$MODDED" --argjson handTol "$HAND_TOL" --argjson rateMin "$RATE_MIN" --argjson rateMax "$RATE_MAX" \
    --slurpfile done <(if [[ -s "$DONE" ]]; then cat "$DONE"; else echo '{}'; fi) '
def dist($p; $q): [range(0; 3) | ($p[.] - $q[.]) * ($p[.] - $q[.])] | add | sqrt;
def flat($p; $q): [0, 2 | ($p[.] - $q[.]) * ($p[.] - $q[.])] | add | sqrt;
def r2: . * 100 | round / 100;
def letters: ascii_downcase | gsub("[^a-z]"; "");
# The PeakXR handshake value (src/PeakXR.Core/PeerTable.cs Handshake): protocol in the low 16 bits, bit 16 = in VR.
def hs_protocol: . % 65536;
def hs_vr: ((. / 65536) | floor) % 2 == 1;
# Checkpoints where A stands still with tracked arms (no ragdoll, emote, climb or carry), for the hand comparison.
def handcps: ["walk-to-host", "ping", "regroup", "grab", "pickup", "holster", "two-hand"];
# --modded-b: B runs PeakXR flat. Its own side of the pose sync, read from b-summary.json and the handshake and
# checkpoint lines; replaces the vanilla "B received no event 151" check.
def moddedb:
  ($s[0]) as $sum | ($sum.peakxr // {}) as $px | ($px.pose151 // {}) as $ps | ($ps.results // {}) as $res
  | ($done[0].sent151 // 0) as $sent
  | [$b[] | select(.type == "handshake")] as $hs
  | ([$b[] | select(.type == "checkpoint")] | map({key: .name, value: .}) | from_entries) as $bcp
  | if $sum.peakxrLoaded == null then "FAIL b-summary.json has no modded-B fields (old observer? mise run build-observer)"
    elif $sum.peakxrLoaded != true then "FAIL B has no PeakXR loaded: install it (docs/testing.md, Modded B) or drop --modded-b"
    else
    "PASS B runs PeakXR \($px.version) (PEAKXR_RUNTIME=\($px.runtime))",
    (if $px.runtime == "Disabled" and ($px.vrEverActive | not) and ($sum.xrEverActive | not) and ($sum.stereoEver | not)
     then "PASS B stayed flat: runtime Disabled, no VR session, no XR device, no stereo camera"
     else "FAIL B did not stay flat: runtime \($px.runtime), VR session \($px.vrEverActive), XR device \($sum.xrEverActive), stereo camera \($sum.stereoEver)" end),
    ([$px.features[]? | select(.state != "Active" and .reason != "VR disabled" and .reason != "not requested")] as $bad
     | ([$px.features[]? | select(.id == "xr.bootstrap")] | first) as $xr
     | if ($px.features // []) == [] then "FAIL B logged no PeakXR feature report"
       elif $bad != [] then "FAIL PeakXR features on B: \($bad | map("\(.id) \(.state) (\(.reason))") | join(", "))"
       elif $xr == null or $xr.state != "Skipped" then "FAIL xr.bootstrap on B is \($xr.state // "missing"), not Skipped"
       else "PASS PeakXR features on B: every VR-only one skipped, none failed" end),
    "INFO PeakXR active on B: \([$px.features[]? | select(.state == "Active") | .id] | join(", ")); patched methods per feature: \($px.patches // {} | to_entries | map("\(.key) \(.value)") | join(", "))",
    (([$hs[] | select(.b != null and (.b | hs_protocol) > 0 and (.b | hs_vr | not))] | first) as $mine
     | ([$hs[] | select(.a != null and (.a | hs_vr))] | first) as $theirs
     | if $mine == null then "FAIL B never published a flat PeakXR handshake (the PeakXR property without the VR bit)"
       elif $theirs == null then "FAIL B never saw a VR handshake from A"
       elif ($mine.b | hs_protocol) != ($theirs.a | hs_protocol) then "FAIL handshake protocols differ: B \($mine.b | hs_protocol), A \($theirs.a | hs_protocol)"
       else "PASS handshake: B \($mine.b) (flat), A \($theirs.a) (VR), protocol \($mine.b | hs_protocol)" end),
    (($res.Accepted // 0) as $acc
     | if ($px.acceptProbe | not) then "FAIL B could not probe PeakXR.Core.RemotePoseStream.Accept"
       elif ($ps.received // 0) == 0 then "FAIL B received no event 151 from A"
       elif $acc == 0 then "FAIL PeakXR on B accepted none of the \($ps.received) pose packets"
       else "PASS B received \($ps.received) pose packets from A, PeakXR accepted \($acc)" end),
    (if ($res.Malformed // 0) > 0 then "FAIL PeakXR on B rejected \($res.Malformed) pose packet(s) from A as malformed or implausible" else empty end),
    (if ($ps.received // 0) == 0 then empty
     elif $ps.meanBusy >= $rateMin and $ps.meanBusy <= $rateMax
     then "PASS pose rate on B: \($ps.meanBusy | r2)/s over the \($ps.busySeconds) s with packets (limits \($rateMin) to \($rateMax))"
     else "FAIL pose rate on B: \($ps.meanBusy | r2)/s over the \($ps.busySeconds) s with packets, outside \($rateMin) to \($rateMax)" end),
    # Received time is B main-thread time: a screenshot or a level load bunches packets, and the bucket then limits them.
    "INFO pose stream on B: peak \($ps.peak1s // 0) in 1 s (receiver cap 40/s, burst 10), \($res.RateLimited // 0) rate-limited, \($res.OutOfOrder // 0) out of order, \(($ps.received // 0) - ([$res[]] | add // 0)) dropped before the stream",
    (if $sent == 0 then empty
     elif ($ps.received // 0) * 2 >= $sent then "PASS B received \($ps.received) of the \($sent) pose packets A sent"
     else "FAIL B received only \($ps.received) of the \($sent) pose packets A sent" end),
    ([$a[] | select(.status == "pass" and (.name as $n | handcps | index($n)))
      | . as $x | $bcp[$x.name] as $y
      | if $x.poseL == null or $x.poseR == null then "SKIP \($x.name) hands: the arms of A were off the tracked pose"
        elif $y == null then "FAIL \($x.name) hands: B logged no checkpoint"
        elif ($y.vrDriven | not) then "FAIL \($x.name) hands: B did not drive the avatar of A from its pose"
        elif $y.boneL == null or $y.boneR == null then "FAIL \($x.name) hands: B logged no hand bones of A"
        # A hand holding an item is judged against the own hand bone of A (what the client of A shows: the item
        # follows the controller free of an arm that cannot reach it); a free hand against the tracked pose of A.
        else ([dist($y.boneL; $x.poseL), dist($y.boneR; $x.poseR)] | max) as $et
          | (if $x.boneL == null or $x.boneR == null then null else [dist($y.boneL; $x.boneL), dist($y.boneR; $x.boneR)] | max end) as $ebv
          | ([(if ($x.holdL // false) and $x.boneL != null then dist($y.boneL; $x.boneL) else dist($y.boneL; $x.poseL) end),
              (if ($x.holdR // false) and $x.boneR != null then dist($y.boneR; $x.boneR) else dist($y.boneR; $x.poseR) end)] | max) as $e
          | ([(if $x.holdL // false then "left" else empty end), (if $x.holdR // false then "right" else empty end)] | join("+")) as $held
          | (if $ebv == null then "?" else ($ebv | r2) end) as $eb
          | (if $x.boneHead == null or $y.boneHead == null then "?" else dist($y.boneHead; $x.boneHead) | r2 end) as $eh
          | "\(if $e <= $handTol then "PASS" else "FAIL" end) \($x.name) hands: on B \($e | r2) m from what A shows (\(if $held == "" then "both free, tracked pose" else "\($held) holding, own bone" end)); \($et | r2) m from the tracked hands of A, \($eb) m from its own hand bones, head bone \($eh) m"
        end]) as $lines
    | ($lines[], (if any($lines[]; startswith("PASS")) then empty
                  else "FAIL hands: no checkpoint compared the avatar hands of A on B (\(handcps | join(", ")))" end)),
    (if $sum.sent151 == 0 then "PASS B sent no event 151" else "FAIL B sent event 151 \($sum.sent151) time(s)" end),
    (if $sum.bepinexErrors == 0 then "PASS B logged no PeakXR or BepInEx errors"
     else "FAIL B logged \($sum.bepinexErrors) mod error(s): \($sum.firstBepinexErrors | map(.e) | join(" | "))" end),
    (if ($px.warnings // 0) == 0 then empty
     else "INFO PeakXR warned \($px.warnings) time(s) on B: \($px.firstWarnings | map(.e) | join(" | "))" end)
    end;
# Every checkpoint MpScript writes (SelfTestRunner.MpTest.cs); one A never wrote means its script stopped early.
def expected: ["join", "walk-to-host", "emote", "ping", "scene-button", "board", "regroup", "luggage", "grab",
  "drop", "pickup", "throw", "eat", "holster", "climb", "two-hand", "feed", "feed-refuse", "reach-deny", "reach",
  "kick-mode", "carry", "pack", "pal-jump", "campfire-far", "spectate", "save-untouched"];
($b | map(select(.type == "checkpoint")) | sort_by(.t)) as $bcps
| ($bcps | map({key: .name, value: .}) | from_entries) as $bcp
| ($b | map(select(.type == "rpc") | .rpc)) as $rpcs
| ($b | map(select(.type == "sample"))) as $samples
| ($s[0]) as $sum
| ($a | map(select(.name == "join")) | first) as $join
| def prevT($y): ([$bcps[] | select(.t < $y.t) | .t] | max) // 0;
  def nextT($y): ([$bcps[] | select(.t > $y.t) | .t] | min) // 1e18;
  def seen($y): prevT($y) as $p | [$rpcs[] | select(.t > $p and .t <= $y.t + $lag)];
  def after($y): prevT($y) as $p | nextT($y) as $n | [$rpcs[] | select(.t > $p and .t < $n)];
  def from_a: select(.local | not);
  def prevcp($y): [$bcps[] | select(.t < $y.t)] | last;
  # One line per step-specific check, for a checkpoint A passed and B answered.
  def step($x; $y):
    if $x.name == "walk-to-host" or $x.name == "regroup" then
      (if $y.localPos == null then "FAIL \($x.name): B logged no position of its own"
       else flat($y.pos; $y.localPos) as $d
         | "\(if $d <= 1.5 + $pos then "PASS" else "FAIL" end) \($x.name): B sees A \($d | r2) m away (goal 1.5)" end)
    elif $x.name == "emote" then
      (($x.note // "") | (capture("hovered .(?<e>[A-Za-z ]*)") | .e) // "" | letters) as $want
      | [seen($y)[] | from_a | select(.name == "RPCA_PlayRemove") | .args[0].s // ""] as $got
      | if $want == "" then "FAIL emote: A named no emote in its note"
        elif any($got[]; letters | contains($want)) then "PASS emote: B saw A play \($want)"
        else "FAIL emote: A played \($want), B saw \($got)" end
    elif $x.name == "ping" then
      [seen($y)[] | from_a | select(.name == "ReceivePoint_Rpc") | .args[0]] as $pts
      | if $pts == [] then "FAIL ping: B got no ReceivePoint_Rpc from A"
        elif $x.ping == null then "FAIL ping: A logged no ping point"
        else ([$pts[] | dist(.; $x.ping)] | min) as $d
          | "\(if $d <= $land then "PASS" else "FAIL" end) ping: B got the point \($d | r2) m from where A saw it" end
    elif $x.name == "board" then
      (if $y.scene != null and $y.scene != "Airport" and $y.scene == $x.scene then "PASS board: B is on \($y.scene) too"
       else "FAIL board: A on \($x.scene), B on \($y.scene)" end),
      (if ($x.note // "") | test("shortcut") then "INFO board: A did not board by hand: \($x.note)" else empty end)
    elif $x.name == "luggage" then
      ((($x.note // "") | capture("opened (?<n>[0-9]+)") | .n | tonumber) // 0) as $n
      | ([seen($y)[] | select(.name == "OpenLuggageRPC")] | length) as $got
      | if $n == 0 then "INFO luggage: A opened no case (enough items already lay around)"
        elif $got >= $n then "PASS luggage: B saw \($got) OpenLuggageRPC for \($n) case(s) A opened"
        else "FAIL luggage: A opened \($n) case(s), B saw \($got) OpenLuggageRPC" end
    elif $x.name == "drop" or $x.name == "throw" then
      (if any(seen($y)[]; (.local | not) and .name == "DropItemRpc") then "PASS \($x.name): B got DropItemRpc from A"
       else "FAIL \($x.name): B got no DropItemRpc from A" end),
      # A threw facing B: where it landed lies on the line from A to where B stands (MP run 13: 2 degrees off).
      (if $x.name != "throw" then empty
       elif $x.landing == null or $y.localPos == null then "FAIL throw: no landing from A or no position of B"
       else [$x.landing[0] - $x.pos[0], $x.landing[2] - $x.pos[2]] as $f
         | [$y.localPos[0] - $x.pos[0], $y.localPos[2] - $x.pos[2]] as $t
         | (($f[0] * $f[0] + $f[1] * $f[1]) | sqrt) as $fl
         | (($t[0] * $t[0] + $t[1] * $t[1]) | sqrt) as $tl
         | (if $fl * $tl == 0 then 180
            else ((($f[0] * $t[0] + $f[1] * $t[1]) / ($fl * $tl)) | if . > 1 then 1 elif . < -1 then -1 else . end
                  | acos * 180 / 3.14159265) end) as $off
         | "\(if $fl >= 1.5 and $off <= 35 then "PASS" else "FAIL" end) throw: landed \($fl | r2) m out, \($off | round) deg off the line to B" end)
    elif $x.name == "eat" then
      (if any(seen($y)[]; (.local | not) and .name == "Consume") then "PASS eat: B saw A consume it"
       else "FAIL eat: B got no Consume from A" end)
    elif $x.name == "holster" then
      [seen($y)[] | from_a | select(.name == "EquipSlotRpc")] as $eq
      | ([$eq[] | select(.args[0] == -1) | .t] | min) as $stow
      | if $stow != null and any($eq[]; .args[0] >= 0 and .t > $stow) then "PASS holster: B saw A stow the item and draw it again"
        else "FAIL holster: B did not see a stow then a draw (EquipSlotRpc \([$eq[] | .args[0]]))" end
    elif $x.name == "climb" then
      (if any(seen($y)[]; (.local | not) and .name == "StartClimbRpc") then "PASS climb: B got StartClimbRpc from A"
       else "FAIL climb: B got no StartClimbRpc from A" end)
    elif $x.name == "feed" then
      (if any(seen($y)[]; .local and .name == "GetFedItemRPC")
          and any(seen($y)[]; (.local | not) and .name == "SendFeedDataRPC")
       then "PASS feed: B was fed by A (SendFeedDataRPC, GetFedItemRPC on B)"
       else "FAIL feed: B missed SendFeedDataRPC from A or GetFedItemRPC on itself" end)
    elif $x.name == "feed-refuse" then
      (if any(after($y)[]; .name == "GetFedItemRPC" or .name == "SendFeedDataRPC")
       then "FAIL feed-refuse: B got a feed RPC for a refused item"
       else "PASS feed-refuse: B got no feed RPC" end)
    elif $x.name == "reach-deny" then
      (if any(after($y)[]; .name == "RPCA_Fall" and .local) then "FAIL reach-deny: B fell (RPCA_Fall) from a refused grip"
       else "PASS reach-deny: B was not knocked down" end)
    elif $x.name == "reach" then
      (if $y.localWeb == null or $y.localSinceGrab == null then "FAIL reach: B logged no Web or grab state (old observer?)"
       elif $y.localWeb > 0.05 then "FAIL reach: B still webbed (\($y.localWeb | r2)) after the reach from A"
       # CharacterGrabbing.Reach sets the helped client own sinceGrabFriend to 0 (:275); Web alone could be 0 untouched.
       elif $y.localSinceGrab > 5 then "FAIL reach: B was last grabbed \($y.localSinceGrab | r2) s before the checkpoint"
       else "PASS reach: the reach from A caught B \($y.localSinceGrab | r2) s before and freed it (Web \($y.localWeb | r2))" end)
    elif $x.name == "pack" then
      prevcp($y) as $p
      | (if $y.localPack == null then "FAIL pack: B logged no pack state (old observer?)"
         elif $p != null and $p.localPack == false then "FAIL pack: B wore no pack before the step, so A held nothing"
         elif ($y.localPack | not) then "FAIL pack: B lost its pack to the hold from A"
         elif any(after($y)[]; .name == "PutInBackpackRPC" or .name == "RPCAddItemToBackpack")
         then "FAIL pack: an item went into the pack of B from a plain hold"
         else "PASS pack: B still wears its pack and nothing went in" end)
    elif $x.name == "kick-mode" then
      (if any(seen($y)[]; .name == "RPCA_Fall" and .local) then "PASS kick-mode: the shove knocked B down"
       else "FAIL kick-mode: B logged no RPCA_Fall from the shove" end)
    elif $x.name == "carry" then
      ([seen($y)[] | from_a | select(.name == "RPCA_StartCarry") | .t] | min) as $lift
      | ([seen($y)[] | from_a | select(.name == "RPCA_Drop") | .t] | max) as $down
      | if $lift == null or $down == null then "FAIL carry: B missed RPCA_StartCarry or RPCA_Drop from A"
        # MP run 13: lift and drop 1 ms apart, so B was never carried (B woke 3 s after passing out and the game
        # dropped it, CharacterCarrying.Update :29-31). A carries for 2 s (MpCarryHold) before the put-down.
        elif any(seen($y)[]; .local and .name == "RPCA_UnPassOut" and .t >= $lift and .t <= $down)
        then "FAIL carry: B woke (RPCA_UnPassOut) while carried"
        elif $down - $lift < 1.5 then "FAIL carry: A put B down \(($down - $lift) | r2) s after lifting"
        else "PASS carry: B saw A lift it and put it down \(($down - $lift) | r2) s later" end
    elif $x.name == "pal-jump" then
      (if any(seen($y)[]; .name == "JumpRpc" and .local and .args[0] == true) then "PASS pal-jump: B was boosted (JumpRpc isPalJump)"
       else "FAIL pal-jump: B logged no pal-jump JumpRpc" end)
    elif $x.name == "campfire-far" then
      (if any(after($y)[]; .name == "Light_Rpc") then "FAIL campfire-far: B saw Light_Rpc with the host away"
       else "PASS campfire-far: the fire stayed unlit on B" end)
    elif $x.name == "spectate" then
      (if $y.fullyPassedOut == true then "PASS spectate: B sees A passed out"
       else "FAIL spectate: B does not see A passed out" end)
    elif $x.name == "scene-button" then
      (if any(after($y)[]; .name == "RPCA_Trigger") then "FAIL scene-button: B saw RPCA_Trigger from a refused press"
       else "PASS scene-button: no trigger reached B" end)
    else empty end;
  [
    (if ($b | any(.type == "join")) then "PASS B saw A join"
     else "FAIL B never saw a remote player join" end),
    (if $join == null then "FAIL A wrote no join checkpoint"
     elif $join.versionOk and $join.inLobby and $join.masterIsHost then
       "PASS A version check: lobby \($join.lobbyVersion) = local \($join.localVersion), in lobby, master is host"
     else "FAIL A join: versionOk=\($join.versionOk) inLobby=\($join.inLobby) masterIsHost=\($join.masterIsHost) (\($join.note))" end),
    ([$a[] | .name] as $names | [expected[] | select(. as $e | $names | index($e) | not)] as $missing
     | if $missing == [] then "PASS A wrote every checkpoint"
       else "FAIL A wrote no checkpoint for: \($missing | join(", "))" end),
    ($a[] | . as $x | $bcp[$x.name] as $y |
      if $x.status == "skip" then "SKIP \($x.name): \($x.note)"
      elif $x.status != "pass" then "FAIL \($x.name): A step failed: \($x.note)"
      elif $y == null then "FAIL \($x.name): B logged no checkpoint"
      elif ($y.remotePresent | not) then "FAIL \($x.name): B saw no remote player"
      elif $y.t - $x.t > $hold then "FAIL \($x.name): B answered \(($y.t - $x.t) | r2) s late, after A moved on"
      else
        (if $x.pos == null or $y.pos == null then "FAIL \($x.name) position: missing"
         else dist($x.pos; $y.pos) as $d
           | "\(if $d <= $pos then "PASS" else "FAIL" end) \($x.name) position: \($d | r2) m apart" end),
        (if $x.heldId == $y.itemId then "PASS \($x.name) held item: \($y.item // "none")"
         else
           # B can write its checkpoint before the equip RPC from A arrives: accept the first matching B sample within LAG_TOL.
           ([$samples[] | select(.t >= $y.t and .t <= $y.t + $lag and .itemId == $x.heldId)] | first) as $late
           | if $late != null then
               "PASS \($x.name) held item: \($late.item // "none") after \(($late.t - $y.t) | r2) s (network lag)"
             else "FAIL \($x.name) held item: A \($x.item // "none") (\($x.heldId)), B sees \($y.item // "none") (\($y.itemId)) for \($lag) s" end
         end),
        (if $x.landing == null then empty
         elif ($y.groundItems // []) == [] then "FAIL \($x.name) landing: B sees no item \($x.itemId) on the ground"
         else ([$y.groundItems[] | dist(.; $x.landing)] | min) as $d
           | "\(if $d <= $land then "PASS" else "FAIL" end) \($x.name) landing: nearest item \($d | r2) m from where A saw it" end),
        step($x; $y)
      end),
    # Emotes A played outside its emote step (a hand pose read as a gesture): a player would see them, so list them.
    ($bcp["emote"] as $e
     | (if $e == null then [] else [seen($e)[] | .t] end) as $mine
     | [$rpcs[] | select((.local | not) and .name == "RPCA_PlayRemove" and (.t as $t | $mine | index($t) | not))
        | .t as $t | "\(.args[0].s // "?") in \([$bcps[] | select(.t >= $t) | .name] | first // "the end")"] as $stray
     | if $stray == [] then empty else "INFO B saw \($stray | length) emote(s) from A outside the emote step: \($stray | join(", "))" end),
    ([$a[] | select(.status == "skip") | .name] as $sk
     | if $sk == [] then empty else "INFO \($sk | length) of \($a | length) checkpoints skipped: \($sk | join(", "))" end),
    (if $sum.saveSame == null then "FAIL B logged no quicksave fingerprints (observer without the progress guard?)"
     elif $sum.saveSame then "PASS B quicksave unchanged: \($sum.saveEnd)"
     else "FAIL B quicksave changed: \($sum.saveStart) -> \($sum.saveEnd)" end),
    (if $sum.exceptions == 0 then "PASS B logged no exceptions"
     else "FAIL B logged \($sum.exceptions) exception(s): \($sum.firstExceptions | map(.e) | join(" | "))" end),
    (if $sum.unknownEventErrors == 0 then "PASS B logged no unknown-event errors"
     else "FAIL B logged \($sum.unknownEventErrors) unknown-event error(s)" end),
    (if $modded then moddedb
     elif $sum.event151 == 0 then "PASS B received no event 151"
     else "FAIL B received event 151 \($sum.event151) time(s)" end),
    (if $modded or $sum.peakxrLoaded != true then empty
     else "FAIL B has PeakXR loaded: switch it back to vanilla (docs/testing.md, Modded B) or pass --modded-b" end),
    "INFO B ended: \($sum.endReason); \($sum.checkpoints) checkpoints, \($sum.samples) samples, \($sum.errors) logged errors"
  ]
| .[]
' || echo "FAIL checker: jq could not read the logs"

if [[ -s "$DONE" ]]; then
    # A run without VR skips every VR step, and one that stopped early never reached the rest: neither proves the mod.
    # A modded B is a handshake peer, so net.pose on A streams to it; a vanilla B never is one.
    jq -r --argjson modded "$MODDED" \
        '"\(if (.sent151 == 0) != $modded then "PASS" else "FAIL" end) A sent event 151 \(.sent151) time(s)",
        (if .reason == "script done" then "PASS A finished its script" else "FAIL A stopped early: \(.reason)" end),
        (if .vr then "PASS A ran in VR" else "FAIL A ran without VR: every VR step skipped" end),
        (if .fail == 0 then empty else "FAIL A logged \(.fail) failed step(s) (see a-mptest.log)" end),
        "INFO A ended: \(.reason); \(.pass) pass, \(.fail) fail, \(.skip) skip, vr=\(.vr)"' "$DONE"
else
    echo "FAIL a-done.json missing: the VR side did not finish"
fi
}

out="$(checks)"
echo "$out"
! grep -q '^FAIL' <<<"$out"
