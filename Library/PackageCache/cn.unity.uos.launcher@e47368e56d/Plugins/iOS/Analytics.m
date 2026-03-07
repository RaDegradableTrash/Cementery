#import <pthread.h>
#import <UIKit/UIKit.h>
#import <AppTrackingTransparency/AppTrackingTransparency.h>
#import <AdSupport/AdSupport.h>

typedef const char * (*IDFACompletionCallback)(const char *idfa, const char *error);

char* tj_strdup(const char* string) {
    if (string == NULL)
        return NULL;
    char* res = (char*)malloc(strlen(string) + 1);
    strcpy(res, string);
    return res;
}

const char *get_idfv() {
    if (![[UIDevice currentDevice] respondsToSelector:@selector(identifierForVendor)]) {
        return tj_strdup("Device doesn't support identifierForVendor");
    }
    
    NSUUID *vendorId = [UIDevice currentDevice].identifierForVendor;
    
    if (!vendorId) {
        return tj_strdup("identifierForVendor is nil");
    }
    
    NSString *uuidString = vendorId.UUIDString;
    
    if (uuidString == nil || uuidString.length == 0) {
        return tj_strdup("Invalid UUID string");
    }
    
    return tj_strdup([uuidString UTF8String]);
}

void get_authorized_idfa(IDFACompletionCallback callback) {
    if (![ASIdentifierManager class]) {
        if (callback) {
            callback(NULL, tj_strdup("AdSupport framework not available"));
        }
        return;
    }
    
    ASIdentifierManager *manager = [ASIdentifierManager sharedManager];
    NSUUID *idfa = manager.advertisingIdentifier;
    NSString *idfaString = idfa.UUIDString;
    
    if (idfaString && idfaString.length > 0) {
        if (callback) {
            callback(tj_strdup([idfaString UTF8String]), NULL);
        }
    } else {
        if (callback) {
            callback(NULL, tj_strdup("Invalid or empty IDFA"));
        }
    }
}

void handle_tracking_authorization_status(ATTrackingManagerAuthorizationStatus status, IDFACompletionCallback callback) {
    switch (status) {
        case ATTrackingManagerAuthorizationStatusAuthorized:
            get_authorized_idfa(callback);
            break;
            
        case ATTrackingManagerAuthorizationStatusDenied:
            if (callback) {
                callback(NULL, tj_strdup("User denied tracking permission"));
            }
            break;
            
        case ATTrackingManagerAuthorizationStatusRestricted:
            if (callback) {
                callback(NULL, tj_strdup("Tracking is restricted (e.g., parental controls)"));
            }
            break;
            
        case ATTrackingManagerAuthorizationStatusNotDetermined:
            if (callback) {
                callback(NULL, tj_strdup("Tracking permission not determined"));
            }
            break;
            
        default:
            if (callback) {
                callback(NULL, tj_strdup("Unknown tracking authorization status"));
            }
            break;
    }
}

void handle_legacy_idfa(IDFACompletionCallback callback) {
    if (![ASIdentifierManager class]) {
        if (callback) {
            callback(NULL, tj_strdup("AdSupport framework not available"));
        }
        return;
    }
    
    ASIdentifierManager *manager = [ASIdentifierManager sharedManager];
    
    if (!manager.advertisingTrackingEnabled) {
        if (callback) {
            callback(NULL, tj_strdup("Advertising tracking is disabled in settings"));
        }
        return;
    }
    
    get_authorized_idfa(callback);
}


void request_idfa(IDFACompletionCallback callback) {
    if (@available(iOS 14, *)) {
        [ATTrackingManager requestTrackingAuthorizationWithCompletionHandler:^(ATTrackingManagerAuthorizationStatus status) {
            dispatch_async(dispatch_get_main_queue(), ^{
                handle_tracking_authorization_status(status, callback);
            });
        }];
    } else {
        handle_legacy_idfa(callback);
    }
}