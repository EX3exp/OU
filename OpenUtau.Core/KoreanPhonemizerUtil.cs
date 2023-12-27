using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using OpenUtau.Core.Ustx;
using static OpenUtau.Api.Phonemizer;

namespace OpenUtau.Core {
    /// <summary>
    /// static class that performs Korean Phoneme Variation, Jamo separation, Jamo merging, etc. 
    /// </summary>
    public static class KoreanPhonemizerUtil {
        /// <summary>
        /// First hangeul consonants, ordered in unicode sequence.
        /// <br/><br/>유니코드 순서대로 정렬된 한국어 초성들입니다.
        /// </summary>
        const string FIRST_CONSONANTS = "ㄱㄲㄴㄷㄸㄹㅁㅂㅃㅅㅆㅇㅈㅉㅊㅋㅌㅍㅎ";
        /// <summary>
        /// Middle hangeul vowels, ordered in unicode sequence.
        /// <br/><br/>유니코드 순서대로 정렬된 한국어 중성들입니다.
        /// </summary>
        const string MIDDLE_VOWELS = "ㅏㅐㅑㅒㅓㅔㅕㅖㅗㅘㅙㅚㅛㅜㅝㅞㅟㅠㅡㅢㅣ";

        /// <summary>
        /// Last hangeul consonants, ordered in unicode sequence.
        /// <br/><br/>유니코드 순서대로 정렬된 한국어 종성들입니다.
        /// </summary>
        const string LAST_CONSONANTS = " ㄱㄲㄳㄴㄵㄶㄷㄹㄺㄻㄼㄽㄾㄿㅀㅁㅂㅄㅅㅆㅇㅈㅊㅋㅌㅍㅎ"; // The first blank(" ") is needed because Hangeul may not have lastConsonant.

        /// <summary>
        /// unicode index of 가
        /// </summary>
        const ushort HANGEUL_UNICODE_START = 0xAC00;

        /// <summary>
        /// unicode index of 힣
        /// </summary>
        const ushort HANGEUL_UNICODE_END = 0xD79F;

        /// <summary>
        /// A hashtable of basicsounds - ㄱ/ㄷ/ㅂ/ㅅ/ㅈ.
        /// <br/><br/>예사소리 테이블입니다.
        /// </summary>
        static readonly Hashtable basicSounds = new Hashtable() {
            ["ㄱ"] = 0,
            ["ㄷ"] = 1,
            ["ㅂ"] = 2,
            ["ㅈ"] = 3,
            ["ㅅ"] = 4
        };

        /// <summary>
        /// A hashtable of aspirate sounds - ㅋ/ㅌ/ㅍ/ㅊ/(ㅌ).
        /// <br/>[4] is "ㅌ", it will be used when conducting phoneme variation - 격음화(거센소리되기).
        /// <br/><br/>거센소리 테이블입니다. 
        /// <br/>[4]의 중복값 "ㅌ"은 오타가 아니며 격음화(거센소리되기) 수행 시에 활용됩니다.
        /// </summary>
        static readonly Hashtable aspirateSounds = new Hashtable() {
            [0] = "ㅋ",
            [1] = "ㅌ",
            [2] = "ㅍ",
            [3] = "ㅊ",
            [4] = "ㅌ"
        };

        /// <summary>
        /// A hashtable of fortis sounds - ㄲ/ㄸ/ㅃ/ㅆ/ㅉ.
        /// <br/><br/>된소리 테이블입니다. 
        /// </summary>
        static readonly Hashtable fortisSounds = new Hashtable() {
            [0] = "ㄲ",
            [1] = "ㄸ",
            [2] = "ㅃ",
            [3] = "ㅉ",
            [4] = "ㅆ"
        };

        /// <summary>
        /// A hashtable of nasal sounds - ㄴ/ㅇ/ㅁ.
        /// <br/><br/>비음 테이블입니다. 
        /// </summary>
        static readonly Hashtable nasalSounds = new Hashtable() {
            ["ㄴ"] = 0,
            ["ㅇ"] = 1,
            ["ㅁ"] = 2
        };


        /// <summary>
        /// Confirms if input string is hangeul.
        /// <br/><br/>입력 문자열이 한글인지 확인합니다.
        /// </summary>
        /// <param name = "character"> A string of Hangeul character. 
        /// <br/>(Example: "가", "!가", "가.")</param>
        /// <returns> Returns true when input string is Hangeul, otherwise false. </returns>
        public static bool IsHangeul(string? character) {

            ushort unicodeIndex;
            bool isHangeul;
            if ((character != null) && character.StartsWith('!')) {
                // Automatically deletes ! from start.
                // Prevents error when user uses ! as a phonetic symbol.  
                unicodeIndex = Convert.ToUInt16(character.TrimStart('!')[0]);
                isHangeul = !(unicodeIndex < HANGEUL_UNICODE_START || unicodeIndex > HANGEUL_UNICODE_END);
            } 
            else if (character != null) {
                try {
                    unicodeIndex = Convert.ToUInt16(character[0]);
                    isHangeul = !(unicodeIndex < HANGEUL_UNICODE_START || unicodeIndex > HANGEUL_UNICODE_END);
                } 
                catch {
                    isHangeul = false;
                }

            } 
            else {
                isHangeul = false;
            }

            return isHangeul;
        }
        /// <summary>
        /// Separates complete hangeul string's first character in three parts - firstConsonant(초성), middleVowel(중성), lastConsonant(종성).
        /// <br/>입력된 문자열의 0번째 글자를 초성, 중성, 종성으로 분리합니다.
        /// </summary>
        /// <param name="character"> A string of complete Hangeul character.
        /// <br/>(Example: '냥') 
        /// </param>
        /// <returns>{firstConsonant(초성), middleVowel(중성), lastConsonant(종성)}
        /// (ex) {"ㄴ", "ㅑ", "ㅇ"}
        /// </returns>
        public static Hashtable Separate(string character) {

            int hangeulIndex; // unicode index of hangeul - unicode index of '가' (ex) '냥'

            int firstConsonantIndex; // (ex) 2
            int middleVowelIndex; // (ex) 2
            int lastConsonantIndex; // (ex) 21

            string firstConsonant; // (ex) "ㄴ"
            string middleVowel; // (ex) "ㅑ"
            string lastConsonant; // (ex) "ㅇ"

            Hashtable separatedHangeul; // (ex) {[0]: "ㄴ", [1]: "ㅑ", [2]: "ㅇ"}


            hangeulIndex = Convert.ToUInt16(character[0]) - HANGEUL_UNICODE_START;

            // seperates lastConsonant
            lastConsonantIndex = hangeulIndex % 28;
            hangeulIndex = (hangeulIndex - lastConsonantIndex) / 28;

            // seperates middleVowel
            middleVowelIndex = hangeulIndex % 21;
            hangeulIndex = (hangeulIndex - middleVowelIndex) / 21;

            // there's only firstConsonant now
            firstConsonantIndex = hangeulIndex;

            // separates character
            firstConsonant = FIRST_CONSONANTS[firstConsonantIndex].ToString();
            middleVowel = MIDDLE_VOWELS[middleVowelIndex].ToString();
            lastConsonant = LAST_CONSONANTS[lastConsonantIndex].ToString();

            separatedHangeul = new Hashtable() {
                [0] = firstConsonant,
                [1] = middleVowel,
                [2] = lastConsonant
            };


            return separatedHangeul;
        }

        /// <summary>
        /// merges separated hangeul into complete hangeul. (Example: {[0]: "ㄱ", [1]: "ㅏ", [2]: " "} => "가"})
        /// <para>자모로 쪼개진 한글을 합쳐진 한글로 반환합니다.</para>
        /// </summary>
        /// <param name="separated">separated Hangeul. </param>
        /// <returns>Returns complete Hangeul Character.</returns>
        public static string Merge(Hashtable separatedHangeul){
            
            int firstConsonantIndex; // (ex) 2
            int middleVowelIndex; // (ex) 2
            int lastConsonantIndex; // (ex) 21

            char firstConsonant = ((string)separatedHangeul[0])[0]; // (ex) "ㄴ"
            char middleVowel = ((string)separatedHangeul[1])[0]; // (ex) "ㅑ"
            char lastConsonant = ((string)separatedHangeul[2])[0]; // (ex) "ㅇ"

            if (firstConsonant == ' ') {firstConsonant = 'ㅇ';}

            firstConsonantIndex = FIRST_CONSONANTS.IndexOf(firstConsonant); // 초성 인덱스
            middleVowelIndex = MIDDLE_VOWELS.IndexOf(middleVowel); // 중성 인덱스
            lastConsonantIndex = LAST_CONSONANTS.IndexOf(lastConsonant); // 종성 인덱스
 
            int mergedCode = HANGEUL_UNICODE_START + (firstConsonantIndex * 21 + middleVowelIndex) * 28 + lastConsonantIndex;
            
            string result = Convert.ToChar(mergedCode).ToString();
            Debug.Print("Hangeul merged: " + $"{firstConsonant} + {middleVowel} + {lastConsonant} = " + result);
            return result;
        }

        /// <summary>
        /// Conducts phoneme variation with two characters input. <br/>※ This method is for only when there are more than one characters, so when there is single character only, Please use Variate(string character).  
        /// <br/><br/>두 글자를 입력받아 음운변동을 진행합니다. <br/>※ 두 글자 이상이 아닌 단일 글자에서 음운변동을 적용할 경우, 이 메소드가 아닌 Variate(string character) 메소드를 사용해야 합니다.
        /// </summary>
        /// <param name="firstCharSeparated"> Separated table of first target.
        /// <br/> 첫 번째 글자를 분리한 해시테이블 
        /// <br/><br/>(Example: {[0]="ㅁ", [1]="ㅜ", [2]="ㄴ"} - 문)
        /// </param>
        /// <param name="nextCharSeparated"> Separated table of second target.
        /// <br/>두 번째 글자를 분리한 해시테이블
        /// <br/><br/>(Example: {[0]="ㄹ", [1]="ㅐ", [2]=" "} - 래)
        /// </param>
        /// <param name="returnCharIndex"> 0: returns result of first target character only. 
        /// <br/>1: returns result of second target character only. <br/>else: returns result of both target characters. <br/>
        /// <br/>0: 첫 번째 타겟 글자의 음운변동 결과만 반환합니다.
        /// <br/>1: 두 번째 타겟 글자의 음운변동 결과만 반환합니다. <br/>나머지 값: 두 타겟 글자의 음운변동 결과를 모두 반환합니다. <br/>
        /// <br/>(Example(0): {[0]="ㅁ", [1]="ㅜ", [2]="ㄹ"} - 물)
        /// <br/>(Example(1): {[0]="ㄹ", [1]="ㅐ", [2]=" "} - 래)
        /// <br/>(Example(-1): {[0]="ㅁ", [1]="ㅜ", [2]="ㄹ", [3]="ㄹ", [4]="ㅐ", [5]=" "} - 물래)
        /// </param>
        /// <returns> Example: when returnCharIndex = 0: {[0]="ㅁ", [1]="ㅜ", [2]="ㄹ"} - 물)
        /// <br/> Example: when returnCharIndex = 1: {[0]="ㄹ", [1]="ㅐ", [2]=" "} - 래)
        /// <br/> Example: when returnCharIndex = -1: {[0]="ㅁ", [1]="ㅜ", [2]="ㄹ", [3]="ㄹ", [4]="ㅐ", [5]=" "} - 물래)
        /// </returns>
        private static Hashtable Variate(Hashtable firstCharSeparated, Hashtable nextCharSeparated, int returnCharIndex = -1) {

            string firstLastConsonant = (string)firstCharSeparated[2]; // 문래 에서 ㄴ, 맑다 에서 ㄺ
            string nextFirstConsonant = (string)nextCharSeparated[0]; // 문래 에서 ㄹ, 맑다 에서 ㄷ

            // 1. 연음 적용 + ㅎ탈락
            if ((!firstLastConsonant.Equals(" ")) && nextFirstConsonant.Equals("ㅎ")) {
                if (basicSounds.Contains(firstLastConsonant)) {
                    // 착하다 = 차카다
                    nextFirstConsonant = (string)aspirateSounds[basicSounds[firstLastConsonant]];
                    firstLastConsonant = " ";
                } else {
                    // 뻔한 = 뻔안 (아래에서 연음 적용되서 뻐난 됨)
                    nextFirstConsonant = "ㅇ";
                }
            }

            if (nextFirstConsonant.Equals("ㅇ") && (! firstLastConsonant.Equals(" "))) {
                // ㄳ ㄵ ㄶ ㄺ ㄻ ㄼ ㄽ ㄾ ㄿ ㅀ ㅄ 일 경우에도 분기해서 연음 적용
                if (firstLastConsonant.Equals("ㄳ")) {
                    firstLastConsonant = "ㄱ";
                    nextFirstConsonant = "ㅅ";
                } 
                else if (firstLastConsonant.Equals("ㄵ")) {
                    firstLastConsonant = "ㄴ";
                    nextFirstConsonant = "ㅈ";
                } 
                else if (firstLastConsonant.Equals("ㄶ")) {
                    firstLastConsonant = "ㄴ";
                    nextFirstConsonant = "ㅎ";
                } 
                else if (firstLastConsonant.Equals("ㄺ")) {
                    firstLastConsonant = "ㄹ";
                    nextFirstConsonant = "ㄱ";
                } 
                else if (firstLastConsonant.Equals("ㄼ")) {
                    firstLastConsonant = "ㄹ";
                    nextFirstConsonant = "ㅂ";
                } 
                else if (firstLastConsonant.Equals("ㄽ")) {
                    firstLastConsonant = "ㄹ";
                    nextFirstConsonant = "ㅅ";
                } 
                else if (firstLastConsonant.Equals("ㄾ")) {
                    firstLastConsonant = "ㄹ";
                    nextFirstConsonant = "ㅌ";
                } 
                else if (firstLastConsonant.Equals("ㄿ")) {
                    firstLastConsonant = "ㄹ";
                    nextFirstConsonant = "ㅍ";
                } 
                else if (firstLastConsonant.Equals("ㅀ")) {
                    firstLastConsonant = "ㄹ";
                    nextFirstConsonant = "ㅎ";
                } 
                else if (firstLastConsonant.Equals("ㅄ")) {
                    firstLastConsonant = "ㅂ";
                    nextFirstConsonant = "ㅅ";
                } 
                else if (firstLastConsonant.Equals("ㅇ") && nextFirstConsonant.Equals("ㅇ")) {
                    // Do nothing
                } 
                else {
                    // 겹받침 아닐 때 연음
                    nextFirstConsonant = firstLastConsonant;
                    firstLastConsonant = " ";
                }
            }


            // 1. 유기음화 및 ㅎ탈락 1
            if (firstLastConsonant.Equals("ㅎ") && (! nextFirstConsonant.Equals("ㅅ")) && basicSounds.Contains(nextFirstConsonant)) {
                // ㅎ으로 끝나고 다음 소리가 ㄱㄷㅂㅈ이면 / ex) 낳다 = 나타
                firstLastConsonant = " ";
                nextFirstConsonant = (string)aspirateSounds[basicSounds[nextFirstConsonant]];
            } 
            else if (firstLastConsonant.Equals("ㅎ") && (!nextFirstConsonant.Equals("ㅅ")) && nextFirstConsonant.Equals("ㅇ")) {
                // ㅎ으로 끝나고 다음 소리가 없으면 / ex) 낳아 = 나아
                firstLastConsonant = " ";
            } 
            else if (firstLastConsonant.Equals("ㄶ") && (! nextFirstConsonant.Equals("ㅅ")) && basicSounds.Contains(nextFirstConsonant)) {
                // ㄶ으로 끝나고 다음 소리가 ㄱㄷㅂㅈ이면 / ex) 많다 = 만타
                firstLastConsonant = "ㄴ";
                nextFirstConsonant = (string)aspirateSounds[basicSounds[nextFirstConsonant]];
            } 
            else if (firstLastConsonant.Equals("ㅀ") && (! nextFirstConsonant.Equals("ㅅ")) && basicSounds.Contains(nextFirstConsonant)) {
                // ㅀ으로 끝나고 다음 소리가 ㄱㄷㅂㅈ이면 / ex) 끓다 = 끌타
                firstLastConsonant = "ㄹ";
                nextFirstConsonant = (string)aspirateSounds[basicSounds[nextFirstConsonant]];
            }




            // 2-1. 된소리되기 1
            if ((firstLastConsonant.Equals("ㄳ") || firstLastConsonant.Equals("ㄵ") || firstLastConsonant.Equals("ㄽ") || firstLastConsonant.Equals("ㄾ") || firstLastConsonant.Equals("ㅄ") || firstLastConsonant.Equals("ㄼ") || firstLastConsonant.Equals("ㄺ") || firstLastConsonant.Equals("ㄿ")) && basicSounds.Contains(nextFirstConsonant)) {
                // [ㄻ, (ㄶ, ㅀ)<= 유기음화에 따라 예외] 제외한 겹받침으로 끝나고 다음 소리가 예사소리이면
                nextFirstConsonant = (string)fortisSounds[basicSounds[nextFirstConsonant]];
            }

            // 3. 첫 번째 글자의 자음군단순화 및 평파열음화(음절의 끝소리 규칙)
            if (firstLastConsonant.Equals("ㄽ") || firstLastConsonant.Equals("ㄾ") || firstLastConsonant.Equals("ㄼ")) {
                firstLastConsonant = "ㄹ";
            } else if (firstLastConsonant.Equals("ㄵ") || firstLastConsonant.Equals("ㅅ") || firstLastConsonant.Equals("ㅆ") || firstLastConsonant.Equals("ㅈ") || firstLastConsonant.Equals("ㅉ") || firstLastConsonant.Equals("ㅊ") || firstLastConsonant.Equals("ㅌ")) {
                firstLastConsonant = "ㄷ";
            } else if (firstLastConsonant.Equals("ㅃ") || firstLastConsonant.Equals("ㅍ") || firstLastConsonant.Equals("ㄿ") || firstLastConsonant.Equals("ㅄ")) {
                firstLastConsonant = "ㅂ";
            } else if (firstLastConsonant.Equals("ㄲ") || firstLastConsonant.Equals("ㅋ") || firstLastConsonant.Equals("ㄺ") || firstLastConsonant.Equals("ㄳ")) {
                firstLastConsonant = "ㄱ";
            } else if (firstLastConsonant.Equals("ㄻ")) {
                firstLastConsonant = "ㅁ";
            }



            // 2-1. 된소리되기 2
            if (basicSounds.Contains(firstLastConsonant) && basicSounds.Contains(nextFirstConsonant)) {
                // 예사소리로 끝나고 다음 소리가 예사소리이면 / ex) 닭장 = 닥짱
                nextFirstConsonant = (string)fortisSounds[basicSounds[nextFirstConsonant]];
            }
            // else if ((firstLastConsonant.Equals("ㄹ")) && (basicSounds.Contains(nextFirstConsonant))){
            //     // ㄹ로 끝나고 다음 소리가 예사소리이면 / ex) 솔직 = 솔찍
            //     // 본래 관형형 어미 (으)ㄹ과 일부 한자어에서만 일어나는 변동이나, 워낙 사용되는 빈도가 많아서 기본으로 적용되게 해 두
            //     // 려 했으나 좀 아닌 것 같아서 보류하기로 함
            //     nextFirstConsonant = (string)fortisSounds[basicSounds[nextFirstConsonant]];
            // }

            // 1. 유기음화 2
            if (basicSounds.Contains(firstLastConsonant) && nextFirstConsonant.Equals("ㅎ")) {
                // ㄱㄷㅂㅈ(+ㅅ)로 끝나고 다음 소리가 ㅎ이면 / ex) 축하 = 추카, 옷하고 = 오타고
                // ㅅ은 미리 평파열음화가 진행된 것으로 보고 ㄷ으로 간주한다
                nextFirstConsonant = (string)aspirateSounds[basicSounds[firstLastConsonant]];
                firstLastConsonant = " ";
            } 
            else if (nextFirstConsonant.Equals("ㅎ")) {
                nextFirstConsonant = "ㅇ";
            }

            if ((!firstLastConsonant.Equals("")) && nextFirstConsonant.Equals("ㅇ") && (!firstLastConsonant.Equals("ㅇ"))) {
                // 연음 2
                nextFirstConsonant = firstLastConsonant;
                firstLastConsonant = " ";
            }


            // 4. 비음화
            if (firstLastConsonant.Equals("ㄱ") && (!nextFirstConsonant.Equals("ㅇ")) && (nasalSounds.Contains(nextFirstConsonant) || nextFirstConsonant.Equals("ㄹ"))) {
                // ex) 막론 = 망론 >> 망논 
                firstLastConsonant = "ㅇ";
            } else if (firstLastConsonant.Equals("ㄷ") && (!nextFirstConsonant.Equals("ㅇ")) && (nasalSounds.Contains(nextFirstConsonant) || nextFirstConsonant.Equals("ㄹ"))) {
                // ex) 슬롯머신 = 슬론머신
                firstLastConsonant = "ㄴ";
            } else if (firstLastConsonant.Equals("ㅂ") && (!nextFirstConsonant.Equals("ㅇ")) && (nasalSounds.Contains(nextFirstConsonant) || nextFirstConsonant.Equals("ㄹ"))) {
                // ex) 밥먹자 = 밤먹자 >> 밤먹짜
                firstLastConsonant = "ㅁ";
            }

            // 4'. 유음화
            if (firstLastConsonant.Equals("ㄴ") && nextFirstConsonant.Equals("ㄹ")) {
                // ex) 만리 = 말리
                firstLastConsonant = "ㄹ";
            } else if (firstLastConsonant.Equals("ㄹ") && nextFirstConsonant.Equals("ㄴ")) {
                // ex) 칼날 = 칼랄
                nextFirstConsonant = "ㄹ";
            }

            // 4''. ㄹ비음화
            if (nextFirstConsonant.Equals("ㄹ") && nasalSounds.Contains(nextFirstConsonant)) {
                // ex) 담력 = 담녁
                firstLastConsonant = "ㄴ";
            }


            // 4'''. 자음동화
            if (firstLastConsonant.Equals("ㄴ") && nextFirstConsonant.Equals("ㄱ")) {
                // ex) ~라는 감정 = ~라능 감정
                firstLastConsonant = "ㅇ";
            }

            // return results
            if (returnCharIndex == 0) {
                // return result of first target character
                return new Hashtable() {
                    [0] = firstCharSeparated[0],
                    [1] = firstCharSeparated[1],
                    [2] = firstLastConsonant
                };
            } else if (returnCharIndex == 1) {
                // return result of second target character
                return new Hashtable() {
                    [0] = nextFirstConsonant,
                    [1] = nextCharSeparated[1],
                    [2] = nextCharSeparated[2]
                };
            } else {
                // 두 글자 다 반환
                return new Hashtable() {
                    [0] = firstCharSeparated[0],
                    [1] = firstCharSeparated[1],
                    [2] = firstLastConsonant,
                    [3] = nextFirstConsonant,
                    [4] = nextCharSeparated[1],
                    [5] = nextCharSeparated[2]
                };
            }
        }

        /// <summary>
        /// Conducts phoneme variation with one character input. <br/>※ This method is only for when there are single character, so when there are more than one character, Please use Variate(Hashtable firstCharSeparated, Hashtable nextCharSeparated, int returnCharIndex=-1).  
        /// <br/><br/>단일 글자를 입력받아 음운변동을 진행합니다. <br/>※ 단일 글자가 아닌 두 글자 이상에서 음운변동을 적용할 경우, 이 메소드가 아닌 Variate(Hashtable firstCharSeparated, Hashtable nextCharSeparated, int returnCharIndex=-1) 메소드를 사용해야 합니다.
        /// </summary>
        /// <param name="character"> String of single target.
        /// <br/> 음운변동시킬 단일 글자.
        /// </param>
        /// <returns>(Example(삵): {[0]="ㅅ", [1]="ㅏ", [2]="ㄱ"} - 삭)
        /// </returns>
        public static Hashtable Variate(string character) {
            /// 맨 끝 노트에서 음운변동 적용하는 함수
            /// 자음군 단순화와 평파열음화
            Hashtable separated = Separate(character);

            if (separated[2].Equals("ㄽ") || separated[2].Equals("ㄾ") || separated[2].Equals("ㄼ") || separated[2].Equals("ㅀ")) {
                separated[2] = "ㄹ";
            } 
            else if (separated[2].Equals("ㄵ") || separated[2].Equals("ㅅ") || separated[2].Equals("ㅆ") || separated[2].Equals("ㅈ") || separated[2].Equals("ㅉ") || separated[2].Equals("ㅊ")) {
                separated[2] = "ㄷ";
            } 
            else if (separated[2].Equals("ㅃ") || separated[2].Equals("ㅍ") || separated[2].Equals("ㄿ") || separated[2].Equals("ㅄ")) {
                separated[2] = "ㅂ";
            } 
            else if (separated[2].Equals("ㄲ") || separated[2].Equals("ㅋ") || separated[2].Equals("ㄺ") || separated[2].Equals("ㄳ")) {
                separated[2] = "ㄱ";
            } 
            else if (separated[2].Equals("ㄻ")) {
                separated[2] = "ㅁ";
            } 
            else if (separated[2].Equals("ㄶ")) {
                separated[2] = "ㄴ";
            }


            return separated;

        }
        /// <summary>
        /// Conducts phoneme variation with one character input. <br/>※ This method is only for when there are single character, so when there are more than one character, Please use Variate(Hashtable firstCharSeparated, Hashtable nextCharSeparated, int returnCharIndex=-1).  
        /// <br/><br/>단일 글자의 분리된 값을 입력받아 음운변동을 진행합니다. <br/>※ 단일 글자가 아닌 두 글자 이상에서 음운변동을 적용할 경우, 이 메소드가 아닌 Variate(Hashtable firstCharSeparated, Hashtable nextCharSeparated, int returnCharIndex=-1) 메소드를 사용해야 합니다.
        /// </summary>
        /// <param name="separated"> Separated table of target.
        /// <br/> 글자를 분리한 해시테이블 
        /// </param>
        /// <returns>(Example({[0]="ㅅ", [1]="ㅏ", [2]="ㄺ"}): {[0]="ㅅ", [1]="ㅏ", [2]="ㄱ"} - 삭)
        /// </returns>
        private static Hashtable Variate(Hashtable separated) {
            /// 맨 끝 노트에서 음운변동 적용하는 함수

            if (separated[2].Equals("ㄽ") || separated[2].Equals("ㄾ") || separated[2].Equals("ㄼ") || separated[2].Equals("ㅀ")) {
                separated[2] = "ㄹ";
            } 
            else if (separated[2].Equals("ㄵ") || separated[2].Equals("ㅅ") || separated[2].Equals("ㅆ") || separated[2].Equals("ㅈ") || separated[2].Equals("ㅉ") || separated[2].Equals("ㅊ")) {
                separated[2] = "ㄷ";
            } 
            else if (separated[2].Equals("ㅃ") || separated[2].Equals("ㅍ") || separated[2].Equals("ㄿ") || separated[2].Equals("ㅄ")) {
                separated[2] = "ㅂ";
            } 
            else if (separated[2].Equals("ㄲ") || separated[2].Equals("ㅋ") || separated[2].Equals("ㄺ") || separated[2].Equals("ㄳ")) {
                separated[2] = "ㄱ";
            } 
            else if (separated[2].Equals("ㄻ")) {
                separated[2] = "ㅁ";
            } 
            else if (separated[2].Equals("ㄶ")) {
                separated[2] = "ㄴ";
            }

            return separated;
        }

        /// <summary>
        /// Conducts phoneme variation with two characters input. <br/>※ This method is for only when there are more than one characters, so when there is single character only, Please use Variate(string character).  
        /// <br/><br/>두 글자를 입력받아 음운변동을 진행합니다. <br/>※ 두 글자 이상이 아닌 단일 글자에서 음운변동을 적용할 경우, 이 메소드가 아닌 Variate(string character) 메소드를 사용해야 합니다.
        /// </summary>
        /// <param name="firstChar"> String of first target.
        /// <br/> 첫 번째 글자.
        /// <br/><br/>(Example: 문)
        /// </param>
        /// <param name="nextChar"> String of second target.
        /// <br/>두 번째 글자.
        /// <br/><br/>(Example: 래)
        /// </param>
        /// <param name="returnCharIndex"> 0: returns result of first target character only. 
        /// <br/>1: returns result of second target character only. <br/>else: returns result of both target characters. <br/>
        /// <br/>0: 첫 번째 타겟 글자의 음운변동 결과만 반환합니다.
        /// <br/>1: 두 번째 타겟 글자의 음운변동 결과만 반환합니다. <br/>나머지 값: 두 타겟 글자의 음운변동 결과를 모두 반환합니다. <br/>
        /// <br/>(Example(0): {[0]="ㅁ", [1]="ㅜ", [2]="ㄹ"} - 물)
        /// <br/>(Example(1): {[0]="ㄹ", [1]="ㅐ", [2]=" "} - 래)
        /// <br/>(Example(-1): {[0]="ㅁ", [1]="ㅜ", [2]="ㄹ", [3]="ㄹ", [4]="ㅐ", [5]=" "} - 물래)
        /// </param>
        /// <returns> Example: when returnCharIndex = 0: {[0]="ㅁ", [1]="ㅜ", [2]="ㄹ"} - 물)
        /// <br/> Example: when returnCharIndex = 1: {[0]="ㄹ", [1]="ㅐ", [2]=" "} - 래)
        /// <br/> Example: when returnCharIndex = -1: {[0]="ㅁ", [1]="ㅜ", [2]="ㄹ", [3]="ㄹ", [4]="ㅐ", [5]=" "} - 물래)
        /// </returns>
        private static Hashtable Variate(string firstChar, string nextChar, int returnCharIndex = 0) {
            // 글자 넣어도 쓸 수 있음
            Hashtable firstCharSeparated = Separate(firstChar);
            Hashtable nextCharSeparated = Separate(nextChar);
            return Variate(firstCharSeparated, nextCharSeparated, returnCharIndex);
        }

        /// <summary>
        /// Conducts phoneme variation automatically with prevNeighbour, note, nextNeighbour.  
        /// <br/><br/> prevNeighbour, note, nextNeighbour를 입력받아 자동으로 음운 변동을 진행합니다.
        /// </summary>
        /// <param name="prevNeighbour"> Note of prev note, if exists(otherwise null).
        /// <br/> 이전 노트 혹은 null.
        /// <br/><br/>(Example: Note with lyric '춘')
        /// </param>
        /// <param name="note"> Note of current note. 
        /// <br/> 현재 노트.
        /// <br/><br/>(Example: Note with lyric '향')
        /// </param>
        /// <param name="nextNeighbour"> Note of next note, if exists(otherwise null).
        /// <br/> 다음 노트 혹은 null.
        /// <br/><br/>(Example: null)
        /// </param>
        /// <returns> Returns phoneme variation result of prevNote, currentNote, nextNote.
        /// <br/>이전 노트, 현재 노트, 다음 노트의 음운변동 결과를 반환합니다.
        /// <br/>Example: 춘 [향] null: {[0]="ㅊ", [1]="ㅜ", [2]=" ", [3]="ㄴ", [4]="ㅑ", [5]="ㅇ", [6]="null", [7]="null", [8]="null"} [추 냥 null]
        /// </returns>
        public static Hashtable Variate(Note? prevNeighbour, Note note, Note? nextNeighbour) {
            // prevNeighbour와 note와 nextNeighbour의 음원변동된 가사를 반환
            // prevNeighbour : VV 정렬에 사용
            // nextNeighbour : VC 정렬에 사용
            // 뒤의 노트가 없으면 리턴되는 값의 6~8번 인덱스가 null로 채워진다.

            /// whereYeonEum : 발음기호 .을 사용하기 위한 변수
            /// .을 사용하면 앞에서 단어가 끝났다고 간주하고, 끝소리에 음운변동을 적용한 후 연음합니다. 
            /// ex) 무 릎 위 [무르퓌] 무 릎. 위[무르뷔]
            /// 
            /// -1 : 해당사항 없음
            /// 0 : 이전 노트를 연음하지 않음
            /// 1 : 현재 노트를 연음하지 않음
            int whereYeonEum = -1;

            string?[] lyrics = new string?[] { prevNeighbour?.lyric, note.lyric, nextNeighbour?.lyric };

            if (!IsHangeul(lyrics[0])) {
                // 앞노트 한국어 아니거나 null일 경우 null처리
                if (lyrics[0] != null) {lyrics[0] = null;}
            } else if (!IsHangeul(lyrics[2])) {
                // 뒤노트 한국어 아니거나 null일 경우 null처리
                if (lyrics[2] != null) {lyrics[2] = null;}
            }
            if ((lyrics[0] != null) && lyrics[0].StartsWith('!')) {
                /// 앞노트 ! 기호로 시작함 ex) [!냥]냥냥
                if (lyrics[0] != null) {lyrics[0] = null;} // 0번가사 없는 걸로 간주함 null냥냥
            }
            if ((lyrics[1] != null) && lyrics[1].StartsWith('!')) {
                /// 중간노트 ! 기호로 시작함 ex) 냥[!냥]냥
                /// 음운변동 미적용
                lyrics[1] = lyrics[1].TrimStart('!');
                if (lyrics[0] != null) {lyrics[0] = null;} // 0번가사 없는 걸로 간주함 null[!냥]냥
                if (lyrics[2] != null) {lyrics[2] = null;} // 2번가사도 없는 걸로 간주함 null[!냥]null
            }
            if ((lyrics[2] != null) && lyrics[2].StartsWith('!')) {
                /// 뒤노트 ! 기호로 시작함 ex) 냥냥[!냥]
                if (lyrics[2] != null) {lyrics[2] = null;} // 2번가사 없는 걸로 간주함 냥냥b
            }

            if ((lyrics[0] != null) && lyrics[0].EndsWith('.')) {
                /// 앞노트 . 기호로 끝남 ex) [냥.]냥냥
                lyrics[0] = lyrics[0].TrimEnd('.');
                whereYeonEum = 0;
            }
            if ((lyrics[1] != null) && lyrics[1].EndsWith('.')) {
                /// 중간노트 . 기호로 끝남 ex) 냥[냥.]냥
                /// 음운변동 없이 연음만 적용
                lyrics[1] = lyrics[1].TrimEnd('.');
                whereYeonEum = 1;
            }
            if ((lyrics[2] != null) && lyrics[2].EndsWith('.')) {
                /// 뒤노트 . 기호로 끝남 ex) 냥냥[냥.]
                /// 중간노트의 발음에 관여하지 않으므로 간단히 . 만 지워주면 된다
                lyrics[2] = lyrics[2].TrimEnd('.');
            }

            // 음운변동 적용 --
            if ((lyrics[0] == null) && (lyrics[2] != null)) {
                /// 앞이 없고 뒤가 있음
                /// null[냥]냥
                if (whereYeonEum == 1) {
                    // 현재 노트에서 단어가 끝났다고 가정
                    Hashtable result = new Hashtable() {
                        [0] = "null", // 앞 글자 없음
                        [1] = "null",
                        [2] = "null"
                    };
                    Hashtable thisNoteSeparated = Variate(Variate(lyrics[1]), Separate(lyrics[2]), -1); // 현 글자 / 끝글자처럼 음운변동시켜서 음원변동 한 번 더 하기

                    result.Add(3, thisNoteSeparated[0]); // 현 글자
                    result.Add(4, thisNoteSeparated[1]);
                    result.Add(5, thisNoteSeparated[2]);

                    result.Add(6, thisNoteSeparated[3]); // 뒤 글자
                    result.Add(7, thisNoteSeparated[4]);
                    result.Add(8, thisNoteSeparated[5]);

                    return result;
                } 
                else {
                    Hashtable result = new Hashtable() {
                        [0] = "null", // 앞 글자 없음
                        [1] = "null",
                        [2] = "null"
                    };

                    Hashtable thisNoteSeparated = Variate(lyrics[1], lyrics[2], -1); // 현글자 뒤글자

                    result.Add(3, thisNoteSeparated[0]); // 현 글자
                    result.Add(4, thisNoteSeparated[1]);
                    result.Add(5, thisNoteSeparated[2]);

                    result.Add(6, thisNoteSeparated[3]); // 뒤 글자 없음
                    result.Add(7, thisNoteSeparated[4]);
                    result.Add(8, thisNoteSeparated[5]);

                    return result;
                }
            } 
            else if ((lyrics[0] != null) && (lyrics[2] == null)) {
                /// 앞이 있고 뒤는 없음
                /// 냥[냥]null
                if (whereYeonEum == 1) {
                    // 현재 노트에서 단어가 끝났다고 가정
                    Hashtable result = Variate(Separate(lyrics[0]), Variate(lyrics[1]), 0); // 첫 글자
                    Hashtable thisNoteSeparated = Variate(Variate(Separate(lyrics[0]), Variate(lyrics[1]), 1)); // 현 글자 / 끝글자처럼 음운변동시켜서 음원변동 한 번 더 하기

                    result.Add(3, thisNoteSeparated[0]); // 현 글자
                    result.Add(4, thisNoteSeparated[1]);
                    result.Add(5, thisNoteSeparated[2]);

                    result.Add(6, "null"); // 뒤 글자 없음
                    result.Add(7, "null");
                    result.Add(8, "null");

                    return result;
                } 
                else if (whereYeonEum == 0) {
                    // 앞 노트에서 단어가 끝났다고 가정 
                    Hashtable result = Variate(Variate(lyrics[0]), Separate(lyrics[1]), 0); // 첫 글자
                    Hashtable thisNoteSeparated = Variate(Variate(Variate(lyrics[0]), Separate(lyrics[1]), 1)); // 첫 글자와 현 글자 / 앞글자를 끝글자처럼 음운변동시켜서 음원변동 한 번 더 하기

                    result.Add(3, thisNoteSeparated[0]); // 현 글자
                    result.Add(4, thisNoteSeparated[1]);
                    result.Add(5, thisNoteSeparated[2]);

                    result.Add(6, "null"); // 뒤 글자 없음
                    result.Add(7, "null");
                    result.Add(8, "null");

                    return result;
                } 
                else {
                    Hashtable result = Variate(lyrics[0], lyrics[1], 0); // 첫 글자
                    Hashtable thisNoteSeparated = Variate(Variate(lyrics[0], lyrics[1], 1)); // 첫 글자와 현 글자 / 뒷글자 없으니까 글자 혼자 있는걸로 음운변동 한 번 더 시키기

                    result.Add(3, thisNoteSeparated[0]); // 현 글자
                    result.Add(4, thisNoteSeparated[1]);
                    result.Add(5, thisNoteSeparated[2]);

                    result.Add(6, "null"); // 뒤 글자 없음
                    result.Add(7, "null");
                    result.Add(8, "null");

                    return result;
                }
            } 
            else if ((lyrics[0] != null) && (lyrics[2] != null)) {
                /// 앞도 있고 뒤도 있음
                /// 냥[냥]냥
                if (whereYeonEum == 1) {
                    // 현재 노트에서 단어가 끝났다고 가정 / 무 [릎.] 위
                    Hashtable result = Variate(Separate(lyrics[0]), Variate(lyrics[1]), 1); // 첫 글자
                    Hashtable thisNoteSeparated = Variate(Variate(Separate(lyrics[0]), Variate(lyrics[1]), 1), Separate(lyrics[2]), -1);// 현글자와 다음 글자 / 현 글자를 끝글자처럼 음운변동시켜서 음원변동 한 번 더 하기

                    result.Add(3, thisNoteSeparated[0]); // 현 글자
                    result.Add(4, thisNoteSeparated[1]);
                    result.Add(5, thisNoteSeparated[2]);

                    result.Add(6, thisNoteSeparated[3]); // 뒤 글자
                    result.Add(7, thisNoteSeparated[4]);
                    result.Add(8, thisNoteSeparated[5]);

                    return result;
                } 
                else if (whereYeonEum == 0) {
                    // 앞 노트에서 단어가 끝났다고 가정 / 릎. [위] 놓
                    Hashtable result = Variate(Variate(lyrics[0]), Separate(lyrics[1]), 0); // 첫 글자
                    Hashtable thisNoteSeparated = Variate(Variate(Variate(lyrics[0]), Separate(lyrics[1]), 1), Separate(lyrics[2]), -1); // 현 글자와 뒤 글자 / 앞글자 끝글자처럼 음운변동시켜서 음원변동 한 번 더 하기

                    result.Add(3, thisNoteSeparated[0]); // 현 글자
                    result.Add(4, thisNoteSeparated[1]);
                    result.Add(5, thisNoteSeparated[2]);

                    result.Add(6, thisNoteSeparated[3]); // 뒤 글자
                    result.Add(7, thisNoteSeparated[4]);
                    result.Add(8, thisNoteSeparated[5]);

                    return result;
                } 
                else {
                    Hashtable result = Variate(lyrics[0], lyrics[1], 0);
                    Hashtable thisNoteSeparated = Variate(Variate(lyrics[0], lyrics[1], 1), Separate(lyrics[2]), -1);

                    result.Add(3, thisNoteSeparated[0]); // 현 글자
                    result.Add(4, thisNoteSeparated[1]);
                    result.Add(5, thisNoteSeparated[2]);

                    result.Add(6, thisNoteSeparated[3]); // 뒤 글자
                    result.Add(7, thisNoteSeparated[4]);
                    result.Add(8, thisNoteSeparated[5]);

                    return result;
                }
            } 
            else {
                /// 앞이 없고 뒤도 없음
                /// null[냥]null

                Hashtable result = new Hashtable() {
                    // 첫 글자 >> 비어 있음
                    [0] = "null",
                    [1] = "null",
                    [2] = "null"
                };

                Hashtable thisNoteSeparated = Variate(lyrics[1]); // 현 글자

                result.Add(3, thisNoteSeparated[0]); // 현 글자
                result.Add(4, thisNoteSeparated[1]);
                result.Add(5, thisNoteSeparated[2]);


                result.Add(6, "null"); // 뒤 글자 비어있음
                result.Add(7, "null");
                result.Add(8, "null");

                return result;
            }
        }

        /// <summary>
        /// (for diffsinger phonemizer)
        /// Conducts phoneme variation automatically with prevNeighbour, note, nextNeighbour.  
        /// <br/><br/> prevNeighbour, note, nextNeighbour를 입력받아 자동으로 음운 변동을 진행합니다.
        /// </summary>
        /// <param name="prevNeighbour"> lyric String of prev note, if exists(otherwise null).
        /// <br/> 이전 가사 혹은 null.
        /// <br/><br/>(Example: lyric String with lyric '춘')
        /// </param>
        /// <param name="note"> lyric String of current note. 
        /// <br/> 현재 가사.
        /// <br/><br/>(Example: Note with lyric '향')
        /// </param>
        /// <param name="nextNeighbour"> lyric String of next note, if exists(otherwise null).
        /// <br/> 다음 가사 혹은 null.
        /// <br/><br/>(Example: null)
        /// </param>
        /// <returns> Returns phoneme variation result of prevNote, currentNote, nextNote.
        /// <br/>이전 노트, 현재 노트, 다음 노트의 음운변동 결과를 반환합니다.
        /// <br/>Example: 춘 [향] null: {[0]="ㅊ", [1]="ㅜ", [2]=" ", [3]="ㄴ", [4]="ㅑ", [5]="ㅇ", [6]="null", [7]="null", [8]="null"} [추 냥 null]
        /// </returns>
        public static String Variate(String? prevNeighbour, String note, String? nextNeighbour) {
            // prevNeighbour와 note와 nextNeighbour의 음원변동된 가사를 반환
            // prevNeighbour : VV 정렬에 사용
            // nextNeighbour : VC 정렬에 사용
            // 뒤의 노트가 없으면 리턴되는 값의 6~8번 인덱스가 null로 채워진다.

            /// whereYeonEum : 발음기호 .을 사용하기 위한 변수
            /// .을 사용하면 앞에서 단어가 끝났다고 간주하고, 끝소리에 음운변동을 적용한 후 연음합니다. 
            /// ex) 무 릎 위 [무르퓌] 무 릎. 위[무르뷔]
            /// 
            /// -1 : 해당사항 없음
            /// 0 : 이전 노트를 연음하지 않음
            /// 1 : 현재 노트를 연음하지 않음
            int whereYeonEum = -1;

            string?[] lyrics = new string?[] { prevNeighbour, note, nextNeighbour};

            if (!IsHangeul(lyrics[0])) {
                // 앞노트 한국어 아니거나 null일 경우 null처리
                if (lyrics[0] != null) {lyrics[0] = null;}
            } else if (!IsHangeul(lyrics[2])) {
                // 뒤노트 한국어 아니거나 null일 경우 null처리
                if (lyrics[2] != null) {lyrics[2] = null;}
            }
            if ((lyrics[0] != null) && lyrics[0].StartsWith('!')) {
                /// 앞노트 ! 기호로 시작함 ex) [!냥]냥냥
                if (lyrics[0] != null) {lyrics[0] = null;} // 0번가사 없는 걸로 간주함 null냥냥
            }
            if ((lyrics[1] != null) && lyrics[1].StartsWith('!')) {
                /// 중간노트 ! 기호로 시작함 ex) 냥[!냥]냥
                /// 음운변동 미적용
                lyrics[1] = lyrics[1].TrimStart('!');
                if (lyrics[0] != null) {lyrics[0] = null;} // 0번가사 없는 걸로 간주함 null[!냥]냥
                if (lyrics[2] != null) {lyrics[2] = null;} // 2번가사도 없는 걸로 간주함 null[!냥]null
            }
            if ((lyrics[2] != null) && lyrics[2].StartsWith('!')) {
                /// 뒤노트 ! 기호로 시작함 ex) 냥냥[!냥]
                if (lyrics[2] != null) {lyrics[2] = null;} // 2번가사 없는 걸로 간주함 냥냥b
            }

            if ((lyrics[0] != null) && lyrics[0].EndsWith('.')) {
                /// 앞노트 . 기호로 끝남 ex) [냥.]냥냥
                lyrics[0] = lyrics[0].TrimEnd('.');
                whereYeonEum = 0;
            }
            if ((lyrics[1] != null) && lyrics[1].EndsWith('.')) {
                /// 중간노트 . 기호로 끝남 ex) 냥[냥.]냥
                /// 음운변동 없이 연음만 적용
                lyrics[1] = lyrics[1].TrimEnd('.');
                whereYeonEum = 1;
            }
            if ((lyrics[2] != null) && lyrics[2].EndsWith('.')) {
                /// 뒤노트 . 기호로 끝남 ex) 냥냥[냥.]
                /// 중간노트의 발음에 관여하지 않으므로 간단히 . 만 지워주면 된다
                lyrics[2] = lyrics[2].TrimEnd('.');
            }

            // 음운변동 적용 --
            if ((lyrics[0] == null) && (lyrics[2] != null)) {
                /// 앞이 없고 뒤가 있음
                /// null[냥]냥
                if (whereYeonEum == 1) {
                    // 현재 노트에서 단어가 끝났다고 가정
                    Hashtable result = new Hashtable() {
                        [0] = "null", // 앞 글자 없음
                        [1] = "null",
                        [2] = "null"
                    };
                    Hashtable thisNoteSeparated = Variate(Variate(lyrics[1]), Separate(lyrics[2]), -1); // 현 글자 / 끝글자처럼 음운변동시켜서 음원변동 한 번 더 하기

                    result.Add(3, thisNoteSeparated[0]); // 현 글자
                    result.Add(4, thisNoteSeparated[1]);
                    result.Add(5, thisNoteSeparated[2]);

                    result.Add(6, thisNoteSeparated[3]); // 뒤 글자
                    result.Add(7, thisNoteSeparated[4]);
                    result.Add(8, thisNoteSeparated[5]);

                    return Merge(new Hashtable{
                    [0] = (string)result[3],
                    [1] = (string)result[4],
                    [2] = (string)result[5]});
                } 
                else {
                    Hashtable result = new Hashtable() {
                        [0] = "null", // 앞 글자 없음
                        [1] = "null",
                        [2] = "null"
                    };

                    Hashtable thisNoteSeparated = Variate(lyrics[1], lyrics[2], -1); // 현글자 뒤글자

                    result.Add(3, thisNoteSeparated[0]); // 현 글자
                    result.Add(4, thisNoteSeparated[1]);
                    result.Add(5, thisNoteSeparated[2]);

                    result.Add(6, thisNoteSeparated[3]); // 뒤 글자 없음
                    result.Add(7, thisNoteSeparated[4]);
                    result.Add(8, thisNoteSeparated[5]);

                    return Merge(new Hashtable{
                    [0] = (string)result[3],
                    [1] = (string)result[4],
                    [2] = (string)result[5]});
                }
            } 
            else if ((lyrics[0] != null) && (lyrics[2] == null)) {
                /// 앞이 있고 뒤는 없음
                /// 냥[냥]null
                if (whereYeonEum == 1) {
                    // 현재 노트에서 단어가 끝났다고 가정
                    Hashtable result = Variate(Separate(lyrics[0]), Variate(lyrics[1]), 0); // 첫 글자
                    Hashtable thisNoteSeparated = Variate(Variate(Separate(lyrics[0]), Variate(lyrics[1]), 1)); // 현 글자 / 끝글자처럼 음운변동시켜서 음원변동 한 번 더 하기

                    result.Add(3, thisNoteSeparated[0]); // 현 글자
                    result.Add(4, thisNoteSeparated[1]);
                    result.Add(5, thisNoteSeparated[2]);

                    result.Add(6, "null"); // 뒤 글자 없음
                    result.Add(7, "null");
                    result.Add(8, "null");

                    return Merge(new Hashtable{
                    [0] = (string)result[3],
                    [1] = (string)result[4],
                    [2] = (string)result[5]});
                } 
                else if (whereYeonEum == 0) {
                    // 앞 노트에서 단어가 끝났다고 가정 
                    Hashtable result = Variate(Variate(lyrics[0]), Separate(lyrics[1]), 0); // 첫 글자
                    Hashtable thisNoteSeparated = Variate(Variate(Variate(lyrics[0]), Separate(lyrics[1]), 1)); // 첫 글자와 현 글자 / 앞글자를 끝글자처럼 음운변동시켜서 음원변동 한 번 더 하기

                    result.Add(3, thisNoteSeparated[0]); // 현 글자
                    result.Add(4, thisNoteSeparated[1]);
                    result.Add(5, thisNoteSeparated[2]);

                    result.Add(6, "null"); // 뒤 글자 없음
                    result.Add(7, "null");
                    result.Add(8, "null");

                    return Merge(new Hashtable{
                    [0] = (string)result[3],
                    [1] = (string)result[4],
                    [2] = (string)result[5]});
                } 
                else {
                    Hashtable result = Variate(lyrics[0], lyrics[1], 0); // 첫 글자
                    Hashtable thisNoteSeparated = Variate(Variate(lyrics[0], lyrics[1], 1)); // 첫 글자와 현 글자 / 뒷글자 없으니까 글자 혼자 있는걸로 음운변동 한 번 더 시키기

                    result.Add(3, thisNoteSeparated[0]); // 현 글자
                    result.Add(4, thisNoteSeparated[1]);
                    result.Add(5, thisNoteSeparated[2]);

                    result.Add(6, "null"); // 뒤 글자 없음
                    result.Add(7, "null");
                    result.Add(8, "null");

                    return Merge(new Hashtable{
                    [0] = (string)result[3],
                    [1] = (string)result[4],
                    [2] = (string)result[5]});
                }
            } 
            else if ((lyrics[0] != null) && (lyrics[2] != null)) {
                /// 앞도 있고 뒤도 있음
                /// 냥[냥]냥
                if (whereYeonEum == 1) {
                    // 현재 노트에서 단어가 끝났다고 가정 / 무 [릎.] 위
                    Hashtable result = Variate(Separate(lyrics[0]), Variate(lyrics[1]), 1); // 첫 글자
                    Hashtable thisNoteSeparated = Variate(Variate(Separate(lyrics[0]), Variate(lyrics[1]), 1), Separate(lyrics[2]), -1);// 현글자와 다음 글자 / 현 글자를 끝글자처럼 음운변동시켜서 음원변동 한 번 더 하기

                    result.Add(3, thisNoteSeparated[0]); // 현 글자
                    result.Add(4, thisNoteSeparated[1]);
                    result.Add(5, thisNoteSeparated[2]);

                    result.Add(6, thisNoteSeparated[3]); // 뒤 글자
                    result.Add(7, thisNoteSeparated[4]);
                    result.Add(8, thisNoteSeparated[5]);

                    return Merge(new Hashtable{
                    [0] = (string)result[3],
                    [1] = (string)result[4],
                    [2] = (string)result[5]});
                } 
                else if (whereYeonEum == 0) {
                    // 앞 노트에서 단어가 끝났다고 가정 / 릎. [위] 놓
                    Hashtable result = Variate(Variate(lyrics[0]), Separate(lyrics[1]), 0); // 첫 글자
                    Hashtable thisNoteSeparated = Variate(Variate(Variate(lyrics[0]), Separate(lyrics[1]), 1), Separate(lyrics[2]), -1); // 현 글자와 뒤 글자 / 앞글자 끝글자처럼 음운변동시켜서 음원변동 한 번 더 하기

                    result.Add(3, thisNoteSeparated[0]); // 현 글자
                    result.Add(4, thisNoteSeparated[1]);
                    result.Add(5, thisNoteSeparated[2]);

                    result.Add(6, thisNoteSeparated[3]); // 뒤 글자
                    result.Add(7, thisNoteSeparated[4]);
                    result.Add(8, thisNoteSeparated[5]);

                    return Merge(new Hashtable{
                    [0] = (string)result[3],
                    [1] = (string)result[4],
                    [2] = (string)result[5]});
                } 
                else {
                    Hashtable result = Variate(lyrics[0], lyrics[1], 0);
                    Hashtable thisNoteSeparated = Variate(Variate(lyrics[0], lyrics[1], 1), Separate(lyrics[2]), -1);

                    result.Add(3, thisNoteSeparated[0]); // 현 글자
                    result.Add(4, thisNoteSeparated[1]);
                    result.Add(5, thisNoteSeparated[2]);

                    result.Add(6, thisNoteSeparated[3]); // 뒤 글자
                    result.Add(7, thisNoteSeparated[4]);
                    result.Add(8, thisNoteSeparated[5]);

                    return Merge(new Hashtable{
                    [0] = (string)result[3],
                    [1] = (string)result[4],
                    [2] = (string)result[5]});
                }
            } 
            else {
                /// 앞이 없고 뒤도 없음
                /// null[냥]null
                Hashtable result = new Hashtable() {
                    // 첫 글자 >> 비어 있음
                    [0] = "null",
                    [1] = "null",
                    [2] = "null"
                };

                Hashtable thisNoteSeparated = Variate(lyrics[1]); // 현 글자

                result.Add(3, thisNoteSeparated[0]); // 현 글자
                result.Add(4, thisNoteSeparated[1]);
                result.Add(5, thisNoteSeparated[2]);


                result.Add(6, "null"); // 뒤 글자 비어있음
                result.Add(7, "null");
                result.Add(8, "null");

                return Merge(new Hashtable{
                    [0] = (string)result[3],
                    [1] = (string)result[4],
                    [2] = (string)result[5]
                });
            }
        }
        
        public static Note[] ChangeLyric(Note[] group, string lyric) {
            // for ENUNU Phonemizer
            var oldNote = group[0];
            group[0] = new Note {
                lyric = lyric,
                phoneticHint = oldNote.phoneticHint,
                tone = oldNote.tone,
                position = oldNote.position,
                duration = oldNote.duration,
                phonemeAttributes = oldNote.phonemeAttributes,
            };
            return group;
        }

        public static void RomanizeNotes(Note[][] groups, Dictionary<string, string[]> firstConsonants, Dictionary<string, string[]> vowels, Dictionary<string, string[]> lastConsonants) {
            // for ENUNU Phonemizer
            
            int noteIdx = 0;
            Note[] currentNote;
            Note[]? prevNote = null;
            Note[]? nextNote;
            
            Note? prevNote_;
            Note? nextNote_;


            List<string> ResultLyrics = new List<string>();
            foreach (Note[] group in groups){    
                currentNote = groups[noteIdx];
                if (groups.Length > noteIdx + 1 && IsHangeul(groups[noteIdx + 1][0].lyric)) {
                    nextNote = groups[noteIdx + 1];
                }
                else {
                    nextNote = null;
                }

                if (prevNote != null) {
                    prevNote_ = prevNote[0];
                    if (prevNote[0].position + prevNote.Sum(note => note.duration) != currentNote[0].position) {
                        prevNote_ = null;
                    }
                }
                else {prevNote_ = null;}

                if (nextNote != null) {
                    nextNote_ = nextNote[0];
                
                    if (nextNote[0].position != currentNote[0].position + currentNote.Sum(note => note.duration)) {
                        nextNote_ = null;
                    }
                }
                else{nextNote_ = null;}
            
                string lyric = "";
                Hashtable lyricSeparated = Variate(prevNote_, currentNote[0], nextNote_);
                lyric += firstConsonants[(string)lyricSeparated[3]][0];
                lyric += vowels[(string)lyricSeparated[4]][0];
                lyric += lastConsonants[(string)lyricSeparated[5]][0];
                ResultLyrics.Add(lyric);

                prevNote = currentNote;
                
                noteIdx++;
            }
            Enumerable.Zip(groups, ResultLyrics.ToArray(), ChangeLyric).Last();
        }

        /// <summary>
    /// abstract class for BaseIniManager(https://github.com/Enichan/Ini/blob/master/Ini.cs)
    /// <para>
    /// Note: This class will be NOT USED when implementing child korean phonemizers. This class is only for BaseIniManager.
    /// </para>
    /// </summary>
    public abstract class IniParser
    {
        public struct IniValue {
            private static bool TryParseInt(string text, out int value) {
                int res;
                if (Int32.TryParse(text,
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out res)) {
                    value = res;
                    return true;
                }
                value = 0;
                return false;
            }
            private static bool TryParseDouble(string text, out double value) {
                double res;
                if (Double.TryParse(text,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out res)) {
                    value = res;
                    return true;
                }
                value = Double.NaN;
                return false;
            }
            public string Value;
            public IniValue(object value) {
                var formattable = value as IFormattable;
                if (formattable != null) {
                    Value = formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture);
                } else {
                    Value = value != null ? value.ToString() : null;
                }
            }
            public IniValue(string value) {
                Value = value;
            }
            public bool ToBool(bool valueIfInvalid = false) {
                bool res;
                if (TryConvertBool(out res)) {
                    return res;
                }
                return valueIfInvalid;
            }
            public bool TryConvertBool(out bool result) {
                if (Value == null) {
                    result = default(bool);
                    return false;
                }
                var boolStr = Value.Trim().ToLowerInvariant();
                if (boolStr == "true") {
                    result = true;
                    return true;
                } else if (boolStr == "false") {
                    result = false;
                    return true;
                }
                result = default(bool);
                return false;
            }
            public int ToInt(int valueIfInvalid = 0) {
                int res;
                if (TryConvertInt(out res)) {
                    return res;
                }
                return valueIfInvalid;
            }
            public bool TryConvertInt(out int result) {
                if (Value == null) {
                    result = default(int);
                    return false;
                }
                if (TryParseInt(Value.Trim(), out result)) {
                    return true;
                }
                return false;
            }
            public double ToDouble(double valueIfInvalid = 0) {
                double res;
                if (TryConvertDouble(out res)) {
                    return res;
                }
                return valueIfInvalid;
            }
            public bool TryConvertDouble(out double result) {
                if (Value == null) {
                    result = default(double);
                    return false; ;
                }
                if (TryParseDouble(Value.Trim(), out result)) {
                    return true;
                }
                return false;
            }
            public string GetString() {
                return GetString(true, false);
            }
            public string GetString(bool preserveWhitespace) {
                return GetString(true, preserveWhitespace);
            }
            public string GetString(bool allowOuterQuotes, bool preserveWhitespace) {
                if (Value == null) {
                    return "";
                }
                var trimmed = Value.Trim();
                if (allowOuterQuotes && trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[trimmed.Length - 1] == '"') {
                    var inner = trimmed.Substring(1, trimmed.Length - 2);
                    return preserveWhitespace ? inner : inner.Trim();
                } else {
                    return preserveWhitespace ? Value : Value.Trim();
                }
            }
            public override string ToString() {
                return Value;
            }
            public static implicit operator IniValue(byte o) {
                return new IniValue(o);
            }
            public static implicit operator IniValue(short o) {
                return new IniValue(o);
            }
            public static implicit operator IniValue(int o) {
                return new IniValue(o);
            }
            public static implicit operator IniValue(sbyte o) {
                return new IniValue(o);
            }
            public static implicit operator IniValue(ushort o) {
                return new IniValue(o);
            }
            public static implicit operator IniValue(uint o) {
                return new IniValue(o);
            }
            public static implicit operator IniValue(float o) {
                return new IniValue(o);
            }
            public static implicit operator IniValue(double o) {
                return new IniValue(o);
            }
            public static implicit operator IniValue(bool o) {
                return new IniValue(o);
            }
            public static implicit operator IniValue(string o) {
                return new IniValue(o);
            }
            private static readonly IniValue _default = new IniValue();
            public static IniValue Default { get { return _default; } }
        }
        public class IniFile : IEnumerable<KeyValuePair<string, IniSection>>, IDictionary<string, IniSection> {
            private Dictionary<string, IniSection> sections;
            public IEqualityComparer<string> StringComparer;
            public bool SaveEmptySections;
            public IniFile()
                : this(DefaultComparer) {
            }
            public IniFile(IEqualityComparer<string> stringComparer) {
                StringComparer = stringComparer;
                sections = new Dictionary<string, IniSection>(StringComparer);
            }
            public void Save(string path, FileMode mode = FileMode.Create) {
                using (var stream = new FileStream(path, mode, FileAccess.Write)) {
                    Save(stream);
                }
            }
            public void Save(Stream stream) {
                using (var writer = new StreamWriter(stream)) {
                    Save(writer);
                }
            }
            public void Save(StreamWriter writer) {
                foreach (var section in sections) {
                    if (section.Value.Count > 0 || SaveEmptySections) {
                        writer.WriteLine(string.Format("[{0}]", section.Key.Trim()));
                        foreach (var kvp in section.Value) {
                            writer.WriteLine(string.Format("{0}={1}", kvp.Key, kvp.Value));
                        }
                        writer.WriteLine("");
                    }
                }
            }
            public void Load(string path, bool ordered = false) {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read)) {
                    Load(stream, ordered);
                }
            }
            public void Load(Stream stream, bool ordered = false) {
                using (var reader = new StreamReader(stream)) {
                    Load(reader, ordered);
                }
            }
            public void Load(StreamReader reader, bool ordered = false) {
                IniSection section = null;
                while (!reader.EndOfStream) {
                    var line = reader.ReadLine();
                    if (line != null) {
                        var trimStart = line.TrimStart();
                        if (trimStart.Length > 0) {
                            if (trimStart[0] == '[') {
                                var sectionEnd = trimStart.IndexOf(']');
                                if (sectionEnd > 0) {
                                    var sectionName = trimStart.Substring(1, sectionEnd - 1).Trim();
                                    section = new IniSection(StringComparer) { Ordered = ordered };
                                    sections[sectionName] = section;
                                }
                            } else if (section != null && trimStart[0] != ';') {
                                string key;
                                IniValue val;
                                if (LoadValue(line, out key, out val)) {
                                    section[key] = val;
                                }
                            }
                        }
                    }
                }
            }
            private bool LoadValue(string line, out string key, out IniValue val) {
                var assignIndex = line.IndexOf('=');
                if (assignIndex <= 0) {
                    key = null;
                    val = null;
                    return false;
                }
                key = line.Substring(0, assignIndex).Trim();
                var value = line.Substring(assignIndex + 1);
                val = new IniValue(value);
                return true;
            }
            public bool ContainsSection(string section) {
                return sections.ContainsKey(section);
            }
            public bool TryGetSection(string section, out IniSection result) {
                return sections.TryGetValue(section, out result);
            }
            bool IDictionary<string, IniSection>.TryGetValue(string key, out IniSection value) {
                return TryGetSection(key, out value);
            }
            public bool Remove(string section) {
                return sections.Remove(section);
            }
            public IniSection Add(string section, Dictionary<string, IniValue> values, bool ordered = false) {
                return Add(section, new IniSection(values, StringComparer) { Ordered = ordered });
            }
            public IniSection Add(string section, IniSection value) {
                if (value.Comparer != StringComparer) {
                    value = new IniSection(value, StringComparer);
                }
                sections.Add(section, value);
                return value;
            }
            public IniSection Add(string section, bool ordered = false) {
                var value = new IniSection(StringComparer) { Ordered = ordered };
                sections.Add(section, value);
                return value;
            }
            void IDictionary<string, IniSection>.Add(string key, IniSection value) {
                Add(key, value);
            }
            bool IDictionary<string, IniSection>.ContainsKey(string key) {
                return ContainsSection(key);
            }
            public ICollection<string> Keys {
                get { return sections.Keys; }
            }
            public ICollection<IniSection> Values {
                get { return sections.Values; }
            }
            void ICollection<KeyValuePair<string, IniSection>>.Add(KeyValuePair<string, IniSection> item) {
                ((IDictionary<string, IniSection>)sections).Add(item);
            }
            public void Clear() {
                sections.Clear();
            }
            bool ICollection<KeyValuePair<string, IniSection>>.Contains(KeyValuePair<string, IniSection> item) {
                return ((IDictionary<string, IniSection>)sections).Contains(item);
            }
            void ICollection<KeyValuePair<string, IniSection>>.CopyTo(KeyValuePair<string, IniSection>[] array, int arrayIndex) {
                ((IDictionary<string, IniSection>)sections).CopyTo(array, arrayIndex);
            }
            public int Count {
                get { return sections.Count; }
            }
            bool ICollection<KeyValuePair<string, IniSection>>.IsReadOnly {
                get { return ((IDictionary<string, IniSection>)sections).IsReadOnly; }
            }
            bool ICollection<KeyValuePair<string, IniSection>>.Remove(KeyValuePair<string, IniSection> item) {
                return ((IDictionary<string, IniSection>)sections).Remove(item);
            }
            public IEnumerator<KeyValuePair<string, IniSection>> GetEnumerator() {
                return sections.GetEnumerator();
            }
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() {
                return GetEnumerator();
            }
            public IniSection this[string section] {
                get {
                    IniSection s;
                    if (sections.TryGetValue(section, out s)) {
                        return s;
                    }
                    s = new IniSection(StringComparer);
                    sections[section] = s;
                    return s;
                }
                set {
                    var v = value;
                    if (v.Comparer != StringComparer) {
                        v = new IniSection(v, StringComparer);
                    }
                    sections[section] = v;
                }
            }
            public string GetContents() {
                using (var stream = new MemoryStream()) {
                    Save(stream);
                    stream.Flush();
                    var builder = new StringBuilder(Encoding.UTF8.GetString(stream.ToArray()));
                    return builder.ToString();
                }
            }
            public static IEqualityComparer<string> DefaultComparer = new CaseInsensitiveStringComparer();
            class CaseInsensitiveStringComparer : IEqualityComparer<string> {
                public bool Equals(string x, string y) {
                    return String.Compare(x, y, true) == 0;
                }
                public int GetHashCode(string obj) {
                    return obj.ToLowerInvariant().GetHashCode();
                }

#if JS
        public new bool Equals(object x, object y) {
            var xs = x as string;
            var ys = y as string;
            if (xs == null || ys == null) {
                return xs == null && ys == null;
            }
            return Equals(xs, ys);
        }

        public int GetHashCode(object obj) {
            if (obj is string) {
                return GetHashCode((string)obj);
            }
            return obj.ToStringInvariant().ToLowerInvariant().GetHashCode();
        }
#endif
                }
            }

            public class IniSection : IEnumerable<KeyValuePair<string, IniValue>>, IDictionary<string, IniValue> {
                private Dictionary<string, IniValue> values;

                #region Ordered
                private List<string> orderedKeys;

                public int IndexOf(string key) {
                    if (!Ordered) {
                        throw new InvalidOperationException("Cannot call IndexOf(string) on IniSection: section was not ordered.");
                    }
                    return IndexOf(key, 0, orderedKeys.Count);
                }

                public int IndexOf(string key, int index) {
                    if (!Ordered) {
                        throw new InvalidOperationException("Cannot call IndexOf(string, int) on IniSection: section was not ordered.");
                    }
                    return IndexOf(key, index, orderedKeys.Count - index);
                }

                public int IndexOf(string key, int index, int count) {
                    if (!Ordered) {
                        throw new InvalidOperationException("Cannot call IndexOf(string, int, int) on IniSection: section was not ordered.");
                    }
                    if (index < 0 || index > orderedKeys.Count) {
                        throw new IndexOutOfRangeException("Index must be within the bounds." + Environment.NewLine + "Parameter name: index");
                    }
                    if (count < 0) {
                        throw new IndexOutOfRangeException("Count cannot be less than zero." + Environment.NewLine + "Parameter name: count");
                    }
                    if (index + count > orderedKeys.Count) {
                        throw new ArgumentException("Index and count were out of bounds for the array or count is greater than the number of elements from index to the end of the source collection.");
                    }
                    var end = index + count;
                    for (int i = index; i < end; i++) {
                        if (Comparer.Equals(orderedKeys[i], key)) {
                            return i;
                        }
                    }
                    return -1;
                }

                public int LastIndexOf(string key) {
                    if (!Ordered) {
                        throw new InvalidOperationException("Cannot call LastIndexOf(string) on IniSection: section was not ordered.");
                    }
                    return LastIndexOf(key, 0, orderedKeys.Count);
                }

                public int LastIndexOf(string key, int index) {
                    if (!Ordered) {
                        throw new InvalidOperationException("Cannot call LastIndexOf(string, int) on IniSection: section was not ordered.");
                    }
                    return LastIndexOf(key, index, orderedKeys.Count - index);
                }

                public int LastIndexOf(string key, int index, int count) {
                    if (!Ordered) {
                        throw new InvalidOperationException("Cannot call LastIndexOf(string, int, int) on IniSection: section was not ordered.");
                    }
                    if (index < 0 || index > orderedKeys.Count) {
                        throw new IndexOutOfRangeException("Index must be within the bounds." + Environment.NewLine + "Parameter name: index");
                    }
                    if (count < 0) {
                        throw new IndexOutOfRangeException("Count cannot be less than zero." + Environment.NewLine + "Parameter name: count");
                    }
                    if (index + count > orderedKeys.Count) {
                        throw new ArgumentException("Index and count were out of bounds for the array or count is greater than the number of elements from index to the end of the source collection.");
                    }
                    var end = index + count;
                    for (int i = end - 1; i >= index; i--) {
                        if (Comparer.Equals(orderedKeys[i], key)) {
                            return i;
                        }
                    }
                    return -1;
                }

                public void Insert(int index, string key, IniValue value) {
                    if (!Ordered) {
                        throw new InvalidOperationException("Cannot call Insert(int, string, IniValue) on IniSection: section was not ordered.");
                    }
                    if (index < 0 || index > orderedKeys.Count) {
                        throw new IndexOutOfRangeException("Index must be within the bounds." + Environment.NewLine + "Parameter name: index");
                    }
                    values.Add(key, value);
                    orderedKeys.Insert(index, key);
                }

                public void InsertRange(int index, IEnumerable<KeyValuePair<string, IniValue>> collection) {
                    if (!Ordered) {
                        throw new InvalidOperationException("Cannot call InsertRange(int, IEnumerable<KeyValuePair<string, IniValue>>) on IniSection: section was not ordered.");
                    }
                    if (collection == null) {
                        throw new ArgumentNullException("Value cannot be null." + Environment.NewLine + "Parameter name: collection");
                    }
                    if (index < 0 || index > orderedKeys.Count) {
                        throw new IndexOutOfRangeException("Index must be within the bounds." + Environment.NewLine + "Parameter name: index");
                    }
                    foreach (var kvp in collection) {
                        Insert(index, kvp.Key, kvp.Value);
                        index++;
                    }
                }

                public void RemoveAt(int index) {
                    if (!Ordered) {
                        throw new InvalidOperationException("Cannot call RemoveAt(int) on IniSection: section was not ordered.");
                    }
                    if (index < 0 || index > orderedKeys.Count) {
                        throw new IndexOutOfRangeException("Index must be within the bounds." + Environment.NewLine + "Parameter name: index");
                    }
                    var key = orderedKeys[index];
                    orderedKeys.RemoveAt(index);
                    values.Remove(key);
                }

                public void RemoveRange(int index, int count) {
                    if (!Ordered) {
                        throw new InvalidOperationException("Cannot call RemoveRange(int, int) on IniSection: section was not ordered.");
                    }
                    if (index < 0 || index > orderedKeys.Count) {
                        throw new IndexOutOfRangeException("Index must be within the bounds." + Environment.NewLine + "Parameter name: index");
                    }
                    if (count < 0) {
                        throw new IndexOutOfRangeException("Count cannot be less than zero." + Environment.NewLine + "Parameter name: count");
                    }
                    if (index + count > orderedKeys.Count) {
                        throw new ArgumentException("Index and count were out of bounds for the array or count is greater than the number of elements from index to the end of the source collection.");
                    }
                    for (int i = 0; i < count; i++) {
                        RemoveAt(index);
                    }
                }

                public void Reverse() {
                    if (!Ordered) {
                        throw new InvalidOperationException("Cannot call Reverse() on IniSection: section was not ordered.");
                    }
                    orderedKeys.Reverse();
                }

                public void Reverse(int index, int count) {
                    if (!Ordered) {
                        throw new InvalidOperationException("Cannot call Reverse(int, int) on IniSection: section was not ordered.");
                    }
                    if (index < 0 || index > orderedKeys.Count) {
                        throw new IndexOutOfRangeException("Index must be within the bounds." + Environment.NewLine + "Parameter name: index");
                    }
                    if (count < 0) {
                        throw new IndexOutOfRangeException("Count cannot be less than zero." + Environment.NewLine + "Parameter name: count");
                    }
                    if (index + count > orderedKeys.Count) {
                        throw new ArgumentException("Index and count were out of bounds for the array or count is greater than the number of elements from index to the end of the source collection.");
                    }
                    orderedKeys.Reverse(index, count);
                }

                public ICollection<IniValue> GetOrderedValues() {
                    if (!Ordered) {
                        throw new InvalidOperationException("Cannot call GetOrderedValues() on IniSection: section was not ordered.");
                    }
                    var list = new List<IniValue>();
                    for (int i = 0; i < orderedKeys.Count; i++) {
                        list.Add(values[orderedKeys[i]]);
                    }
                    return list;
                }

                public IniValue this[int index] {
                    get {
                        if (!Ordered) {
                            throw new InvalidOperationException("Cannot index IniSection using integer key: section was not ordered.");
                        }
                        if (index < 0 || index >= orderedKeys.Count) {
                            throw new IndexOutOfRangeException("Index must be within the bounds." + Environment.NewLine + "Parameter name: index");
                        }
                        return values[orderedKeys[index]];
                    }
                    set {
                        if (!Ordered) {
                            throw new InvalidOperationException("Cannot index IniSection using integer key: section was not ordered.");
                        }
                        if (index < 0 || index >= orderedKeys.Count) {
                            throw new IndexOutOfRangeException("Index must be within the bounds." + Environment.NewLine + "Parameter name: index");
                        }
                        var key = orderedKeys[index];
                        values[key] = value;
                    }
                }

                public bool Ordered {
                    get {
                        return orderedKeys != null;
                    }
                    set {
                        if (Ordered != value) {
                            orderedKeys = value ? new List<string>(values.Keys) : null;
                        }
                    }
                }
                #endregion

                public IniSection()
                    : this(IniFile.DefaultComparer) {
                }

                public IniSection(IEqualityComparer<string> stringComparer) {
                    this.values = new Dictionary<string, IniValue>(stringComparer);
                }

                public IniSection(Dictionary<string, IniValue> values)
                    : this(values, IniFile.DefaultComparer) {
                }

                public IniSection(Dictionary<string, IniValue> values, IEqualityComparer<string> stringComparer) {
                    this.values = new Dictionary<string, IniValue>(values, stringComparer);
                }

                public IniSection(IniSection values)
                    : this(values, IniFile.DefaultComparer) {
                }

                public IniSection(IniSection values, IEqualityComparer<string> stringComparer) {
                    this.values = new Dictionary<string, IniValue>(values.values, stringComparer);
                }

                public void Add(string key, IniValue value) {
                    values.Add(key, value);
                    if (Ordered) {
                        orderedKeys.Add(key);
                    }
                }

                public bool ContainsKey(string key) {
                    return values.ContainsKey(key);
                }

                /// <summary>
                /// Returns this IniSection's collection of keys. If the IniSection is ordered, the keys will be returned in order.
                /// </summary>
                public ICollection<string> Keys {
                    get { return Ordered ? (ICollection<string>)orderedKeys : values.Keys; }
                }

                public bool Remove(string key) {
                    var ret = values.Remove(key);
                    if (Ordered && ret) {
                        for (int i = 0; i < orderedKeys.Count; i++) {
                            if (Comparer.Equals(orderedKeys[i], key)) {
                                orderedKeys.RemoveAt(i);
                                break;
                            }
                        }
                    }
                    return ret;
                }

                public bool TryGetValue(string key, out IniValue value) {
                    return values.TryGetValue(key, out value);
                }

                /// <summary>
                /// Returns the values in this IniSection. These values are always out of order. To get ordered values from an IniSection call GetOrderedValues instead.
                /// </summary>
                public ICollection<IniValue> Values {
                    get {
                        return values.Values;
                    }
                }

                void ICollection<KeyValuePair<string, IniValue>>.Add(KeyValuePair<string, IniValue> item) {
                    ((IDictionary<string, IniValue>)values).Add(item);
                    if (Ordered) {
                        orderedKeys.Add(item.Key);
                    }
                }

                public void Clear() {
                    values.Clear();
                    if (Ordered) {
                        orderedKeys.Clear();
                    }
                }

                bool ICollection<KeyValuePair<string, IniValue>>.Contains(KeyValuePair<string, IniValue> item) {
                    return ((IDictionary<string, IniValue>)values).Contains(item);
                }

                void ICollection<KeyValuePair<string, IniValue>>.CopyTo(KeyValuePair<string, IniValue>[] array, int arrayIndex) {
                    ((IDictionary<string, IniValue>)values).CopyTo(array, arrayIndex);
                }

                public int Count {
                    get { return values.Count; }
                }

                bool ICollection<KeyValuePair<string, IniValue>>.IsReadOnly {
                    get { return ((IDictionary<string, IniValue>)values).IsReadOnly; }
                }

                bool ICollection<KeyValuePair<string, IniValue>>.Remove(KeyValuePair<string, IniValue> item) {
                    var ret = ((IDictionary<string, IniValue>)values).Remove(item);
                    if (Ordered && ret) {
                        for (int i = 0; i < orderedKeys.Count; i++) {
                            if (Comparer.Equals(orderedKeys[i], item.Key)) {
                                orderedKeys.RemoveAt(i);
                                break;
                            }
                        }
                    }
                    return ret;
                }

                public IEnumerator<KeyValuePair<string, IniValue>> GetEnumerator() {
                    if (Ordered) {
                        return GetOrderedEnumerator();
                    } else {
                        return values.GetEnumerator();
                    }
                }

                private IEnumerator<KeyValuePair<string, IniValue>> GetOrderedEnumerator() {
                    for (int i = 0; i < orderedKeys.Count; i++) {
                        yield return new KeyValuePair<string, IniValue>(orderedKeys[i], values[orderedKeys[i]]);
                    }
                }

                System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() {
                    return GetEnumerator();
                }

                public IEqualityComparer<string> Comparer { get { return values.Comparer; } }

                public IniValue this[string name] {
                    get {
                        IniValue val;
                        if (values.TryGetValue(name, out val)) {
                            return val;
                        }
                        return IniValue.Default;
                    }
                    set {
                        if (Ordered && !orderedKeys.Contains(name, Comparer)) {
                            orderedKeys.Add(name);
                        }
                        values[name] = value;
                    }
                }

                public static implicit operator IniSection(Dictionary<string, IniValue> dict) {
                    return new IniSection(dict);
                }

                public static explicit operator Dictionary<string, IniValue>(IniSection section) {
                    return section.values;
                }
            }
        }

    /// <summary>
    /// abstract class for Ini Management
    /// To use, child phonemizer should implement this class(BaseIniManager) with its own setting values!
    /// </summary>
    public abstract class BaseIniManager : IniParser{
        protected USinger singer;
        protected IniFile iniFile = new IniFile();
        protected string iniFileName;

        public BaseIniManager() { }

        /// <summary>
        /// if no [iniFileName] in Singer Directory, it makes new [iniFileName] with settings in [IniSetUp(iniFile)].
        /// </summary>
        /// <param name="singer"></param>
        /// <param name="iniFileName"></param>
        public void Initialize(USinger singer, string iniFileName) {
            this.singer = singer;
            this.iniFileName = iniFileName;
            try {
                iniFile.Load($"{singer.Location}/{iniFileName}");
                IniSetUp(iniFile); // you can override IniSetUp() to use.
            } 
            catch {
                IniSetUp(iniFile); // you can override IniSetUp() to use.
            }
       }

        /// <summary>
        /// <para>you can override this method with your own values. </para> 
        /// !! when implement this method, you have to use [SetOrReadThisValue(string sectionName, string keyName, bool/string/int/double value)] when setting or reading values.
        /// <para>(ex)
        /// SetOrReadThisValue("sectionName", "keyName", true);</para>
        /// </summary>
       protected virtual void IniSetUp(IniFile iniFile) {
       }

       /// <summary>
       /// <param name="sectionName"> section's name in .ini config file. </param>
       /// <param name="keyName"> key's name in .ini config file's [sectionName] section. </param>
       /// <param name="defaultValue"> default value to overwrite if there's no valid value in config file. </param>
       /// inputs section name & key name & default value. If there's valid boolean vaule, nothing happens. But if there's no valid boolean value, overwrites current value with default value.
       /// 섹션과 키 이름을 입력받고, bool 값이 존재하면 넘어가고 존재하지 않으면 defaultValue 값으로 덮어씌운다 
       /// </summary>
       protected void SetOrReadThisValue(string sectionName, string keyName, bool defaultValue) {
           iniFile[sectionName][keyName] = iniFile[sectionName][keyName].ToBool(defaultValue);
           iniFile.Save($"{singer.Location}/{iniFileName}");
       }

       /// <summary>
       /// <param name="sectionName"> section's name in .ini config file. </param>
       /// <param name="keyName"> key's name in .ini config file's [sectionName] section. </param>
       /// <param name="defaultValue"> default value to overwrite if there's no valid value in config file. </param>
       /// inputs section name & key name & default value. If there's valid string vaule, nothing happens. But if there's no valid string value, overwrites current value with default value.
       /// 섹션과 키 이름을 입력받고, string 값이 존재하면 넘어가고 존재하지 않으면 defaultValue 값으로 덮어씌운다 
       /// </summary>
       protected void SetOrReadThisValue(string sectionName, string keyName, string defaultValue) {
           if (!iniFile[sectionName].ContainsKey(keyName)) {
               // 키가 존재하지 않으면 새로 값을 넣는다
               iniFile[sectionName][keyName] = defaultValue;
               iniFile.Save($"{singer.Location}/{iniFileName}");
           }
           // 키가 존재하면 그냥 스킵
       }

       /// <summary>
       /// 
       /// <param name="sectionName"> section's name in .ini config file. </param>
       /// <param name="keyName"> key's name in .ini config file's [sectionName] section. </param>
       /// <param name="defaultValue"> default value to overwrite if there's no valid value in config file. </param>
       /// inputs section name & key name & default value. If there's valid int vaule, nothing happens. But if there's no valid int value, overwrites current value with default value.
       /// 섹션과 키 이름을 입력받고, int 값이 존재하면 넘어가고 존재하지 않으면 defaultValue 값으로 덮어씌운다 
       /// </summary>
       protected void SetOrReadThisValue(string sectionName, string keyName, int defaultValue) {
           iniFile[sectionName][keyName] = iniFile[sectionName][keyName].ToInt(defaultValue);
           iniFile.Save($"{singer.Location}/{iniFileName}");
       }

       /// <summary>
       /// <param name="sectionName"> section's name in .ini config file. </param>
       /// <param name="keyName"> key's name in .ini config file's [sectionName] section. </param>
       /// <param name="defaultValue"> default value to overwrite if there's no valid value in config file. </param>
       /// inputs section name & key name & default value. If there's valid double vaule, nothing happens. But if there's no valid double value, overwrites current value with default value.
       /// 섹션과 키 이름을 입력받고, double 값이 존재하면 넘어가고 존재하지 않으면 defaultValue 값으로 덮어씌운다 
       /// </summary>
       protected void SetOrReadThisValue(string sectionName, string keyName, double defaultValue) {
           iniFile[sectionName][keyName] = iniFile[sectionName][keyName].ToDouble(defaultValue);
           iniFile.Save($"{singer.Location}/{iniFileName}");
       }
    }
    }
    
}