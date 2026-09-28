import Toybox.Attention;
import Toybox.Lang;

// Uses Garmin's built-in tone constants, NOT custom Attention.ToneProfile
// note sequences.
//
// Confirmed via a Garmin-acknowledged bug report ("Fenix 8 - No sound from
// app when using ToneProfile", open >1yr, unresolved): newer speaker-based
// devices (fenix 8 among them) can only play Garmin's pre-recorded built-in
// tones, not arbitrary custom tone profiles - the Connect IQ API has no way
// to tell a tone-generator device and a speaker device apart, so has(:playTone)
// returns true on both even though custom ToneProfile arrays silently do
// nothing on the speaker ones. Built-in tones are the only choice that's
// actually reliable across devices.
class Fanfare {

    static function play(soundKey as String) as Void {
        if (!(Toybox.Attention has :playTone)) {
            return;
        }
        Attention.playTone(toneFor(soundKey));
    }

    // Short confirmation blip for the Reward scene transition.
    static function confirm() as Void {
        if (!(Toybox.Attention has :playTone)) {
            return;
        }
        Attention.playTone(Attention.TONE_KEY);
    }

    static function vibrate(tier as Symbol) as Void {
        if (!(Toybox.Attention has :vibrate)) {
            return;
        }
        Attention.vibrate(vibesFor(tier));
    }

    private static function toneFor(soundKey as String) as Attention.Tone {
        if (soundKey.equals("fanfare_long")) {
            return Attention.TONE_SUCCESS; // legendary
        }
        if (soundKey.equals("fanfare")) {
            return Attention.TONE_ALERT_HI; // epic
        }
        if (soundKey.equals("fail")) {
            return Attention.TONE_FAILURE; // cursed
        }
        return Attention.TONE_KEY; // chime (common/rare)
    }

    private static function vibesFor(tier as Symbol) as Array<Attention.VibeProfile> {
        if (tier == :legendary) {
            return [
                new Attention.VibeProfile(100, 200),
                new Attention.VibeProfile(0, 100),
                new Attention.VibeProfile(100, 200),
                new Attention.VibeProfile(0, 100),
                new Attention.VibeProfile(100, 200),
            ];
        }
        if (tier == :epic) {
            return [
                new Attention.VibeProfile(100, 200),
                new Attention.VibeProfile(0, 100),
                new Attention.VibeProfile(100, 200),
            ];
        }
        if (tier == :cursed) {
            return [ new Attention.VibeProfile(40, 600) ];
        }
        // common/rare: single short pulse.
        return [ new Attention.VibeProfile(60, 150) ];
    }
}
