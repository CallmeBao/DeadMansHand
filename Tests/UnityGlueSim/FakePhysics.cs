using System;
using System.Collections;
using UnityEngine;

namespace TheLastKing
{
    /// <summary>Bản giả của LastKingPhysics chỉ trong bộ mô phỏng: đặt xu "tức thì" theo cùng quy tắc tự đặt của bản thật.</summary>
    public sealed class LastKingPhysics : MonoBehaviour
    {
        public float PlacementTimeout = 20f;
        public event Action<int> OnDeposited;
        int owner = -1; float age;
        public int Started, Deposited, Resets;
        public bool IsBusy { get { return owner >= 0; } }

        public bool StartCoin(int ownerSeat, int coinIndex)
        {
            if (owner >= 0) return false;
            owner = ownerSeat; age = 0; Started++;
            return true;
        }
        public void AutoPlace() { Finish(); }
        public void ResetCoins() { owner = -1; Resets++; }

        void Update()
        {
            if (owner < 0) return;
            age += Time.deltaTime;
            if ((owner == 1 && age >= 1.1f) || age >= PlacementTimeout) Finish();
        }

        void Finish()
        {
            int o = owner;
            if (o < 0) return;
            owner = -1; Deposited++;
            if (OnDeposited != null) OnDeposited(o);
        }
    }

    public static class LastKingPhysicsChecks
    {
        public static IEnumerator Run(LastKingPhysics rig, Action<string> note = null) { yield break; }
    }
}
