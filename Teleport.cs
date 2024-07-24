using System.Collections.Generic;
using System.Linq;
using Cinemachine;
using Unity.VisualScripting;
using UnityEngine;

namespace DamianGonzalez.Portals {

    [DefaultExecutionOrder(111)] //110 Movement > 111 Teleport > 112 PortalCamMovement > 113 PortalSetup (rendering) > 114 PortalRenderer
    public class Teleport : MonoBehaviour {

        /*
         * Quick reminder (for visitors and for myself) of how this works.
         * 
         * Player uses elastic plane, so they teleport on Update, the trigger does nothing on them.
         * Other objects follows this plan:
         *  - when they enter the trigger, a clone is made on the other side, but the original (and it physics) keeps on this side
         *  - while they are inside, the clone is updated
         *  - if player crosses sides while the object is still inside, clone and original are swapped (for physics)
         *  - when it exits the trigger, it teleports and the clone is destroyed
         * 
         * That's the plan, BUT...
         * Players have other 2 fallback methods, in case they can't count with the Update method
         * (either because the trigger is too short, or the player too fast, or the PC too slow) 
         * and they only stay inside the trigger a few frames, or none at all, so:
         * 1) they can teleport directly on "OnTriggerEnter", when its speed is greater than the declared limit
         * 2) they can teleport on "OnTriggerExit", when it's actually too late, this is called "emergency teleport"
         * 
         * */
        [HideInInspector] public bool portalIsEnabled = true;
        private Vector3 originalPlanePosition;
        [HideInInspector] public BoxCollider _collider;               //
        [HideInInspector] public Transform plane;                     //
        [HideInInspector] public Transform portal;                    // these variables are automatically writen
        [HideInInspector] public PortalSetup setup;                   // by PortalSetup 
        [HideInInspector] public PortalCamMovement cameraScript;      //
        [HideInInspector] public Teleport otherScript;                //
        [HideInInspector] public bool planeIsInverted; //only for elastic mode
        Teleport otherPortal => otherScript;
        List<Traveller> trackedTravellers = new List<Traveller>();

        // Eval these var
        Vector3 lastFramePosition = Vector3.zero;
        Vector3 deltaMov;
        private void Start() {
            originalPlanePosition = plane.localPosition;
        }

        public void DisableThisPortal() {
            SetEnabled(false);
        }

        public void EnableThisPortal() {
            SetEnabled(true);
        }

        public void SetEnabled(bool _enabled) {
            portalIsEnabled = _enabled;
            gameObject.SetActive(portalIsEnabled);                //trigger (functional)
            cameraScript.gameObject.SetActive(portalIsEnabled);   //camera (functional)
            plane.gameObject.SetActive(portalIsEnabled);          //plane (visual)
        }

        Vector3 GetVelocity(Transform obj) {
            if (obj.TryGetComponent(out Rigidbody rb)) return rb.velocity;
            if (obj.TryGetComponent(out CharacterController cc)) return cc.velocity;
            return Vector3.zero;
        }

        Vector3 TowardDestination(Transform obj) {
            //single-sided: crossing the plane
            if (!setup.doubleSided) return TowardDestinationSingleSided();
            //double-sided: whichever direction the object is going
            return IsGoodSide(obj) ? TowardDestinationSingleSided() : -TowardDestinationSingleSided();
        }

        public Vector3 TowardDestinationSingleSided() => planeIsInverted ? -portal.forward : portal.forward;

        public bool IsGoodSide(Transform obj) {
            //not about facing, but about velocity. where is it going?
            Vector3 velocityOrPosition = GetVelocity(obj);
            return IsGoodSide(velocityOrPosition);
        }
        public bool IsGoodSide(Vector3 velocityOrPosition) {
            //not about facing, but about velocity. where is it going?
            float dotProduct;
            if (velocityOrPosition != Vector3.zero) {
                //it has velocity
                dotProduct = Vector3.Dot(-TowardDestinationSingleSided(), velocityOrPosition);
            } else {
                //it hasn't velocity, let's try with its position (it may fail with very fast objects)
                dotProduct = Vector3.Dot(-TowardDestinationSingleSided(), portal.position - velocityOrPosition);
            }

            //if (setup.debugOptions.verboseDebug) Debug.Log($"{obj.name} crossing. Good side: {dotProduct < 0}");
            return dotProduct < 0;
        }

        public bool IsGoodSideDir(Vector3 dir) => Vector3.Dot(-TowardDestinationSingleSided(), dir) < 0;

        void DoTeleport(Traveller traveller, bool fireEvents = true) {

            if (!setup.doubleSided && !IsGoodSide(traveller.transform)) {
                if (setup.debugOptions.verboseDebug) Debug.Log($"{traveller.transform.name} will not teleport. Reason: not the good side"); 
                return;
            }

            if (traveller.JustTeleported) {
                if (setup.debugOptions.verboseDebug) Debug.Log($"{traveller.transform.name} will not teleport. Reason: too soon");
                return;
            }

            traveller.PreTeleport();

            Vector3 positionOld = traveller.transform.position;
            Quaternion rotOld = traveller.transform.rotation;

            otherPortal.OnTravellerEnterPortal (traveller);

            traveller.Teleport(portal, otherScript.portal);

            OnTravellerExitPortal (traveller, true);

            // Apply Gravity??
            /*
            if (setup.multiGravity.applyMultiGravity) {
                if (objectToTeleport.TryGetComponent(out CustomGravity gr)) {
                    Vector3 oldDirection = gr.down;
                    gr.ChangeGravity(-otherScript.portal.up);

                    //changed?
                    if (setup.debugOptions.verboseDebug && oldDirection != gr.down) {
                        Debug.Log($"Changed gravity from {oldDirection} to {gr.down}");
                    }
                    
                }
            }
            */

            if (traveller.IsLocalPlayer) {
                //avoid miscalulating the distance from previous frame
                lastFramePosition = Vector3.zero;
                otherScript.lastFramePosition = Vector3.zero;

                //refresh camera position before rendering, in order to avoid flickering
                otherScript.cameraScript.Recalculate();
                cameraScript.Recalculate();
                //MoveElasticPlane(.1f);
                //otherScript.MoveElasticPlane(.1f);

                if (setup.afterTeleport.tryResetCameraObject) {
                    //reset player's camera (may solve issues)
                    setup.player.playerCameraTr.gameObject.SetActive(false);
                    setup.player.playerCameraTr.gameObject.SetActive(true);
                }

                if (setup.afterTeleport.tryResetCameraScripts) {
                    //reset scripts in camera
                    foreach (MonoBehaviour scr in setup.player.playerCameraTr.GetComponents<MonoBehaviour>()) {
                        if (scr.isActiveAndEnabled) {
                            scr.enabled = false;
                            scr.enabled = true;
                        }
                    }
                }

                if (setup.debugOptions.pauseWhenPlayerTeleports) Debug.Break();

            } else {
                // If you need to do something to other crossing object, this is when.
                if (setup.debugOptions.pauseWhenOtherTeleports) Debug.Break();
            }

            traveller.PostTeleport();
            //finally, fire event
            if (fireEvents)
                PortalEvents.teleport?.Invoke(
                    setup.groupId,
                    portal,
                    otherScript.portal,
                    traveller,
                    positionOld,
                    rotOld
                );
        }

        void OnTravellerEnterPortal (Traveller traveller) {
            if (setup.debugOptions.verboseDebug) Debug.Log($"{portal.name} : {traveller.transform.name} entering the trigger. ");
            if (!trackedTravellers.Exists(x => x.transform == traveller.transform)) {
                if (setup.debugOptions.verboseDebug) Debug.Log($"{portal.name} : {traveller.transform.name} Added to List. ");

                if (setup.clones.useClones && (!traveller.IsLocalPlayer || setup.clones.clonePlayerToo)) {
                    CreateClone(traveller);
                    traveller.CloneUpdate(portal, otherScript.portal);
                }

                traveller.EnterPortalThreshold(DistanceToPlane(traveller.transform));
                traveller.previousOffsetFromPortal = traveller.transform.position - transform.position;
                trackedTravellers.Add(traveller);

                if(traveller.IsLocalPlayer) {
                    if (setup.surfacePortals.isSurfacePortals && setup.surfacePortals.disableCollidersNearPortals) DisableColliders();
                    /*
                    if (setup.advanced.tryPreventTooFastFailure && 
                    traveller.Rigidbody.velocity.magnitude > setup.advanced.maxSpeedForElasticPlane.Evaluate((int)(1f / Time.deltaTime))) {
                        if (setup.debugOptions.verboseDebug) {
                            Debug.Log(
                                $"Player crossing too fast ({traveller.Rigidbody.velocity.magnitude} m/s at {(int)(1f / Time.deltaTime)} FPS). " +
                                $"Trying to teleport earlier (on trigger enter)."
                            );
                        }
                        DoTeleport(traveller);
                        return;
                    }
                    */
                }
            }
        }  

        void OnTravellerExitPortal (Traveller traveller, bool teleport = false) {
            if (setup.debugOptions.verboseDebug) Debug.Log($"{portal.name} : On trigger exit. {traveller.transform.name} has left the trigger. TresspasProgress value is {traveller.trespassProgress}");
                
            if(!teleport){
                traveller.ExitPortalThreshold(DistanceToPlane(traveller.transform));
                if(traveller.graphicsClone != null){
                    Destroy(traveller.graphicsClone);
                    traveller.graphicsClone = null;
                }
            }
            trackedTravellers.Remove(traveller);

            if(traveller.IsLocalPlayer){
                if(setup.surfacePortals.isSurfacePortals && setup.surfacePortals.disableCollidersNearPortals) RestoreColliders();
                /* Will need to look at EmergencyTeleport Later
                if (setup.advanced.tryEmergencyTeleport) {
                    //if using elastic plane and it's NOT declared as a big portal,
                    //and player crosses too fast (1 or 2 frames), it should consider to be teleported
                    traveller.trespassProgress = DistanceToPlane(traveller.transform); // Move Progress to Traveller, or just make it a call to Distance?

                    if (
                        setup.advanced.useElasticPlane
                        && goodSideWhenEnteredTrigger
                        && traveller.trespassProgress > 0
                        && traveller.trespassProgress <= setup.advanced.maxElasticPlaneValueForEmergency
                    ) {
                        if (setup.debugOptions.verboseDebug) Debug.Log($"Emergency teleport! {traveller.transform.name} has left the trigger. TresspasProgress was {traveller.trespassProgress}");
                        DoTeleport(traveller);
                    }
                }
                */
                //RestorePlaneOriginalPosition();
            }
        }
        void OnTriggerEnter(Collider other) {
            if (other.isTrigger || other.gameObject.layer == LayerMask.NameToLayer("Ignore Reycast")) return;
            
            if (!ThisObjectCanCross(other.transform)) {
                //if (setup.debugOptions.verboseDebug) Debug.Log($"{portal.name} : {other.transform.name} will not teleport(ENTER). Reason: filters");
                return;
            }

            var traveller = new Traveller(other.transform);
            OnTravellerEnterPortal(traveller);
        }  
        void OnTriggerExit(Collider other) {
            if (other.isTrigger || other.gameObject.layer == LayerMask.NameToLayer("Ignore Reycast")) return;

            if (!ThisObjectCanCross(other.transform)) {
                //if (setup.debugOptions.verboseDebug) Debug.Log($"{portal.name} : {other.transform.name} will not teleport(EXIT). Reason: filters");
                return;
            }

            if(trackedTravellers.Exists( x => x.transform == other.transform)){
                if (setup.debugOptions.verboseDebug) Debug.Log($"{portal.name} : {other.name} Traveller Found (EXIT)");
                OnTravellerExitPortal(trackedTravellers.Find( x => x.transform == other.transform));
                return;
            }
        }

        #region SURFACE PORTAL COLLIDERS
        public List<Collider> nearCollidersToDisable = new List<Collider>();

        public void AddColliderToDisable(Collider col) {
            if (!col.isTrigger) {
                nearCollidersToDisable.Add(col);
            }
        }

        public void DisableColliders() {
            foreach (Collider col in nearCollidersToDisable) col.enabled = false;
        }

        public void RestoreColliders() {
            foreach (Collider col in nearCollidersToDisable) col.enabled = true;
        }

        public void GetInitialColliders() {
            foreach (Collider col in Physics.OverlapBox(_collider.center, Vector3.one * 1f)) {
                AddColliderToDisable(col);
            }
        }
        #endregion

        private void Update() {
            HandleTravellers ();

            //UpdateClones();
        }
        void HandleTravellers () {
            for (int i = 0; i < trackedTravellers.Count; i++) {

                Traveller traveller = trackedTravellers[i];
                traveller.PortalUpdate(DistanceToPlane(traveller.transform));
                traveller.CloneUpdate(portal, otherScript.portal);
                /*
                if(traveller.IsLocalPlayer){
                    ManageElasticPlane(traveller);
                }
                */
                Vector3 offsetFromPortal_Pos = traveller.transform.position - transform.position;
                //Quaternion offsetFromPortal_Rot = traveller.transform.rotation * Quaternion.Inverse(transform.rotation);

                int portalSide = System.Math.Sign (Vector3.Dot (offsetFromPortal_Pos, transform.forward));
                int portalSideOld = System.Math.Sign (Vector3.Dot (traveller.previousOffsetFromPortal, transform.forward));

                // Teleport the traveller if it has crossed from one side of the portal to the other
                if (portalSide != portalSideOld && !traveller.JustTeleported) {
                    DoTeleport(traveller);
                    i--;
                } else {
                    //UpdateSliceParams (traveller);
                    traveller.previousOffsetFromPortal = offsetFromPortal_Pos;
                }
            }
        }

        public float DistanceToPlane(Transform obj) {
            return Vector3.Dot(
                -TowardDestination(obj), 
                portal.position - obj.position
            );
        }

        #region  ElasticPlane (Player ONLY)
        bool tresspasProgressInTeleportWindow(float trespass) => (
                trespass >= setup.advanced.elasticPlaneTeleportWindowFrom
                && trespass <= setup.advanced.elasticPlaneTeleportWindowTo
        );

        private void ManageElasticPlane(Traveller traveller) {
            if (traveller.IsLocalPlayer && setup.advanced.useElasticPlane) {

                //move this plane
                MoveElasticPlane(traveller);

                //teleport player when the progress treshold is reached
                if (setup.advanced.useElasticPlane && tresspasProgressInTeleportWindow(traveller.trespassProgress)) {
                    if (setup.debugOptions.verboseDebug) Debug.Log($"teleported because {traveller.trespassProgress} > {setup.advanced.elasticPlaneTeleportWindowFrom}");
    
                    DoTeleport(traveller);
                }
            }
        }

        private void MoveElasticPlane(Traveller traveller, bool forced = false) {
            if (traveller.trespassProgress > setup.advanced.elasticPlaneMinTreshold  && traveller.trespassProgress <= setup.advanced.elasticPlaneTeleportWindowFrom) {
                
                //calculate offset by velocity
                float relativeSpeed = 0;
                if (setup.advanced.dynamicOffsetBasedOnVelocity) {

                    //if player just crossed this frame, don't recalculate deltaMov, use last frame's speed
                    if (lastFramePosition != Vector3.zero) {
                        deltaMov = setup.player.playerCameraTr.position - lastFramePosition;
                    }

                    relativeSpeed = Vector3.Dot(TowardDestination(traveller.transform), deltaMov);
                } 

                
                //calculate offset
                float totalOffset = 
                    setup.advanced.elasticPlaneOffset * 1                           //1. the constant minimum offset
                    + traveller.trespassProgress                                             //2. how much player has crossed
                    + relativeSpeed * setup.advanced.elasticPlaneVelocityFactor     //3. an extra according to velocity
                    ;


                //apply
                plane.localPosition = originalPlanePosition;
                plane.position += TowardDestination(traveller.transform) * totalOffset;

                SetClippingOffset(-(traveller.trespassProgress) + setup.advanced.clippingOffset);
                
            } else {
                //RestorePlaneOriginalPosition();
            }

            lastFramePosition = setup.player.playerCameraTr.position;
        }

        public void RestorePlaneOriginalPosition() {
            plane.localPosition = originalPlanePosition;
            
            SetClippingOffset(setup.advanced.clippingOffset);
            cameraScript.ApplyAdvancedOffset();

        }

        void SetClippingOffset(float value) {
            cameraScript.currentClippingOffset = value;
            otherScript.cameraScript.currentClippingOffset = value;
        }

        #endregion

        #region CLONES

        void CreateClone(Traveller original) {
            if (setup.debugOptions.verboseDebug) Debug.Log($"Creating clone for {original.transform.name}");

            //this clone already exists? remove it
            if (original.graphicsClone != null){
                if (setup.debugOptions.verboseDebug) Debug.Log($"{original.transform.name} already has a Clone");
                return;
            }

            Transform clone = null;
            if(original.useCloneTarget){
                return;
            }else{
                clone = Instantiate(original.transform, original.transform.parent);
            }
            clone.name = "(portal clone) " + original.transform.name;
            original.graphicsClone = clone;
            //destroy some components from itself and childrens, to obtain a simplified version of the object
            foreach (Rigidbody rb in clone.GetComponentsInChildren<Rigidbody>()) Destroy(rb);
            foreach (Collider col in clone.GetComponentsInChildren<Collider>()) Destroy(col);
            foreach (CharacterController cc in clone.GetComponentsInChildren<CharacterController>()) Destroy(cc);
            foreach (AudioListener lis in clone.GetComponentsInChildren<AudioListener>()) Destroy(lis);
            foreach (MonoBehaviour scr in clone.GetComponentsInChildren<MonoBehaviour>()) {
                bool destroyScript = true;
                if (typeof(TMPro.TextMeshPro) != null && scr.GetType() == typeof(TMPro.TextMeshPro)) destroyScript = false;
                if (destroyScript) Destroy(scr);
            }
            clone.gameObject.tag = "Untagged";
        }
        #endregion
        bool ThisObjectCanCross(Transform obj) {
            //negative filter
            if (setup.filters.tagsCannotCross.Contains(obj.tag)) return false;

            //main filter
            switch (setup.filters.otherObjectsCanCross) {
                case PortalSetup.Filters.OthersCanCross.Everything: 
                    return true;

                case PortalSetup.Filters.OthersCanCross.NothingOnlyPlayer:
                    return false;

                case PortalSetup.Filters.OthersCanCross.OnlySpecificTags:
                    if (setup.filters.tagsCanCross.Count > 0 && !setup.filters.tagsCanCross.Contains(obj.tag)) return false;
                    return true;
            }

            return true;
        }
    }
}