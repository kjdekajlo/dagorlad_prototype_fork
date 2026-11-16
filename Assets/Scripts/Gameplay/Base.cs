using System.Collections.Generic;
using UnityEngine;

public class Base : MonoBehaviour
{
    public Team team;
    public List<BuildingSlot> locations = new();
    public GameObject baseFlag;

    public float flagEnter;
    public float stayPeriodToUnlock;
    public bool isLocked = true;
    // Start is called before the first frame update
    void Start()
    {
        if (baseFlag)
        {
            baseFlag.GetComponent<OnTrigger>().AddEvent("Stay", Teams.Tag(Team.Player), (sender, collider) => {
                if (team == Team.Neutral && isLocked == true)
                {
                    isLocked = false;
                    flagEnter = Time.time;
                }
                if (Time.time - flagEnter > stayPeriodToUnlock && isLocked == false)
                {
                    team = Team.Player;
                    UIManager.Instance.UnlockBase(gameObject.name);
                    sender.GetComponent<Animator>().SetTrigger("RISE");
                    sender.transform.GetChild(0).gameObject.SetActive(true);
                    isLocked = true;
                }
            });
            baseFlag.GetComponent<OnTrigger>().AddEvent("Exit", Teams.Tag(Team.Player), (sender, collider) => {
                if (team == Team.Neutral) flagEnter = Time.time;
            });
        }
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
